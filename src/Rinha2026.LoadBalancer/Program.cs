using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

await using RequestAwareLoadBalancer loadBalancer = RequestAwareLoadBalancer.Create(
	Environment.GetEnvironmentVariable("ASPNETCORE_URLS"),
	Environment.GetEnvironmentVariable("BACKEND_ENDPOINTS"));
await loadBalancer.RunAsync();

internal sealed class RequestAwareLoadBalancer : IAsyncDisposable {
	private const int ReceiveBufferSize = 16 * 1024;
	private const int MaxHeaderBytes = 8 * 1024;

	private readonly ConcurrentDictionary<int, Task> connectionTasks = new();
	private readonly Socket listener;
	private readonly BackendEndpoint[] backends;
	private readonly byte[] badGatewayResponse = BuildResponse("502 Bad Gateway");
	private readonly byte[] badRequestResponse = BuildResponse("400 Bad Request");
	private Task? acceptLoopTask;
	private CancellationTokenSource? cancellationTokenSource;
	private bool disposed;
	private int nextBackendIndex;
	private int nextConnectionId;

	private RequestAwareLoadBalancer(int listenPort, BackendEndpoint[] backends) {
		this.backends = backends;
		this.listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) {
			NoDelay = true,
		};
		this.listener.Bind(new IPEndPoint(IPAddress.Any, listenPort));
		this.listener.Listen(backlog: 512);
	}

	public static RequestAwareLoadBalancer Create(string? urls, string? backendEndpoints) {
		BackendEndpoint[] backends = ParseBackends(backendEndpoints);
		return new RequestAwareLoadBalancer(ResolveListenPort(urls), backends);
	}

	public async ValueTask DisposeAsync() {
		if (this.disposed) {
			return;
		}

		await this.StopAsync();
		this.listener.Dispose();
		this.disposed = true;
	}

	public async Task RunAsync(CancellationToken cancellationToken = default) {
		await this.StartAsync(cancellationToken);
		await (this.acceptLoopTask ?? Task.CompletedTask);
	}

	public Task StartAsync(CancellationToken cancellationToken = default) {
		ObjectDisposedException.ThrowIf(this.disposed, this);

		if (this.acceptLoopTask is not null) {
			return Task.CompletedTask;
		}

		this.cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		this.acceptLoopTask = this.AcceptLoopAsync(this.cancellationTokenSource.Token);
		return Task.CompletedTask;
	}

	public async Task StopAsync() {
		CancellationTokenSource? cancellationTokenSource = this.cancellationTokenSource;

		if (cancellationTokenSource is null) {
			return;
		}

		this.cancellationTokenSource = null;
		await cancellationTokenSource.CancelAsync();

		try {
			this.listener.Dispose();
		} catch (ObjectDisposedException) {
		}

		Task? acceptLoopTask = this.acceptLoopTask;
		this.acceptLoopTask = null;

		if (acceptLoopTask is not null) {
			try {
				await acceptLoopTask;
			} catch (OperationCanceledException) {
			} catch (ObjectDisposedException) {
			}
		}

		Task[] activeConnections = [.. this.connectionTasks.Values];

		if (activeConnections.Length > 0) {
			try {
				await Task.WhenAll(activeConnections);
			} catch (OperationCanceledException) {
			}
		}

		cancellationTokenSource.Dispose();
	}

	private async Task AcceptLoopAsync(CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			Socket? socket = null;

			try {
				socket = await this.listener.AcceptAsync(cancellationToken);
				socket.NoDelay = true;
				int connectionId = Interlocked.Increment(ref this.nextConnectionId);
				Task connectionTask = this.HandleClientSafelyAsync(connectionId, socket, cancellationToken);
				this.connectionTasks[connectionId] = connectionTask;
			} catch (OperationCanceledException) {
				break;
			} catch (ObjectDisposedException) {
				break;
			} catch {
				socket?.Dispose();

				if (cancellationToken.IsCancellationRequested) {
					break;
				}
			}
		}
	}

	private async Task HandleClientSafelyAsync(int connectionId, Socket clientSocket, CancellationToken cancellationToken) {
		try {
			await this.HandleClientAsync(clientSocket, cancellationToken);
		} finally {
			this.connectionTasks.TryRemove(connectionId, out _);
			clientSocket.Dispose();
		}
	}

	private async Task HandleClientAsync(Socket clientSocket, CancellationToken cancellationToken) {
		byte[] clientBuffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
		int bufferedClientBytes = 0;
		BackendConnection?[] backendConnections = new BackendConnection?[this.backends.Length];

		try {
			while (!cancellationToken.IsCancellationRequested) {
				int bytesRead = await clientSocket.ReceiveAsync(
					clientBuffer.AsMemory(bufferedClientBytes),
					SocketFlags.None,
					cancellationToken);

				if (bytesRead <= 0) {
					return;
				}

				bufferedClientBytes += bytesRead;

				while (bufferedClientBytes > 0) {
					HttpParseStatus parseStatus = TryParseHttpMessage(clientBuffer, bufferedClientBytes, out int consumedBytes);

					if (parseStatus == HttpParseStatus.Incomplete) {
						if (bufferedClientBytes >= MaxHeaderBytes) {
							await SendAllAsync(clientSocket, this.badRequestResponse, cancellationToken);
						}

						break;
					}

					if (parseStatus == HttpParseStatus.Invalid) {
						await SendAllAsync(clientSocket, this.badRequestResponse, cancellationToken);
						return;
					}

					int backendIndex = this.SelectBackendIndex();
					BackendConnection backendConnection = await GetOrConnectBackendAsync(
						backendConnections,
						backendIndex,
						this.backends[backendIndex],
						cancellationToken);

					if (!await this.TryProxyRequestAsync(
						backendConnections,
						backendIndex,
						backendConnection,
						clientSocket,
						clientBuffer.AsMemory(0, consumedBytes),
						cancellationToken)) {
						return;
					}

					ShiftBufferLeft(clientBuffer, bufferedClientBytes, consumedBytes);
					bufferedClientBytes -= consumedBytes;
				}

				if (bufferedClientBytes >= MaxHeaderBytes) {
					await SendAllAsync(clientSocket, this.badRequestResponse, cancellationToken);
					return;
				}
			}
		} finally {
			ArrayPool<byte>.Shared.Return(clientBuffer);

			foreach (BackendConnection? backendConnection in backendConnections) {
				backendConnection?.Dispose();
			}
		}
	}

	private async Task<bool> TryProxyRequestAsync(
		BackendConnection?[] backendConnections,
		int backendIndex,
		BackendConnection backendConnection,
		Socket clientSocket,
		ReadOnlyMemory<byte> requestPayload,
		CancellationToken cancellationToken) {
		for (int attempt = 0; attempt < 2; attempt++) {
			try {
				await SendAllAsync(backendConnection.Socket, requestPayload, cancellationToken);
				return await this.TryProxyResponseAsync(backendConnection, clientSocket, cancellationToken);
			} catch (IOException) {
			} catch (SocketException) {
			} catch (ObjectDisposedException) {
			}

			backendConnection.Dispose();
			backendConnection = await ReconnectBackendAsync(backendConnections, backendIndex, this.backends[backendIndex], cancellationToken);
		}

		await SendAllAsync(clientSocket, this.badGatewayResponse, cancellationToken);
		return false;
	}

	private async Task<bool> TryProxyResponseAsync(
		BackendConnection backendConnection,
		Socket clientSocket,
		CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			HttpParseStatus parseStatus = TryParseHttpMessage(
				backendConnection.Buffer,
				backendConnection.BufferedBytes,
				out int consumedBytes);

			if (parseStatus == HttpParseStatus.Success) {
				await SendAllAsync(clientSocket, backendConnection.Buffer.AsMemory(0, consumedBytes), cancellationToken);
				ShiftBufferLeft(backendConnection.Buffer, backendConnection.BufferedBytes, consumedBytes);
				backendConnection.BufferedBytes -= consumedBytes;
				return true;
			}

			if (parseStatus == HttpParseStatus.Invalid) {
				await SendAllAsync(clientSocket, this.badGatewayResponse, cancellationToken);
				return false;
			}

			if (backendConnection.BufferedBytes >= MaxHeaderBytes) {
				await SendAllAsync(clientSocket, this.badGatewayResponse, cancellationToken);
				return false;
			}

			int bytesRead = await backendConnection.Socket.ReceiveAsync(
				backendConnection.Buffer.AsMemory(backendConnection.BufferedBytes),
				SocketFlags.None,
				cancellationToken);

			if (bytesRead <= 0) {
				throw new IOException("Backend socket closed before the response was complete.");
			}

			backendConnection.BufferedBytes += bytesRead;
		}

		return false;
	}

	private int SelectBackendIndex() {
		uint index = unchecked((uint)Interlocked.Increment(ref this.nextBackendIndex));
		return (int)(index % (uint)this.backends.Length);
	}

	private static async Task<BackendConnection> GetOrConnectBackendAsync(
		BackendConnection?[] backendConnections,
		int backendIndex,
		BackendEndpoint backend,
		CancellationToken cancellationToken) {
		BackendConnection? existing = backendConnections[backendIndex];

		if (existing is not null) {
			return existing;
		}

		BackendConnection connection = await BackendConnection.ConnectAsync(backend, cancellationToken);
		backendConnections[backendIndex] = connection;
		return connection;
	}

	private static async Task<BackendConnection> ReconnectBackendAsync(
		BackendConnection?[] backendConnections,
		int backendIndex,
		BackendEndpoint backend,
		CancellationToken cancellationToken) {
		backendConnections[backendIndex]?.Dispose();
		BackendConnection connection = await BackendConnection.ConnectAsync(backend, cancellationToken);
		backendConnections[backendIndex] = connection;
		return connection;
	}

	private static byte[] BuildResponse(string status) {
		string header = $"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
		return Encoding.ASCII.GetBytes(header);
	}

	private static int FindCrlf(ReadOnlySpan<byte> buffer, int startIndex) {
		for (int index = startIndex + 1; index < buffer.Length; index++) {
			if ((buffer[index - 1] == (byte)'\r') && (buffer[index] == (byte)'\n')) {
				return index - 1;
			}
		}

		return -1;
	}

	private static int FindHeaderTerminator(ReadOnlySpan<byte> buffer) {
		for (int index = 3; index < buffer.Length; index++) {
			if ((buffer[index - 3] == (byte)'\r') &&
				(buffer[index - 2] == (byte)'\n') &&
				(buffer[index - 1] == (byte)'\r') &&
				(buffer[index] == (byte)'\n')) {
				return index - 3;
			}
		}

		return -1;
	}

	private static int? ParseContentLength(ReadOnlySpan<byte> headers) {
		int scanIndex = 0;

		while (scanIndex < headers.Length) {
			int lineEnd = FindCrlf(headers, scanIndex);

			if (lineEnd < 0) {
				lineEnd = headers.Length;
			}

			ReadOnlySpan<byte> line = headers[scanIndex..lineEnd];

			if (StartsWithAsciiIgnoreCase(line, "Content-Length:"u8)) {
				return ParsePositiveInt32(TrimAsciiWhitespace(line["Content-Length:".Length..]));
			}

			if (StartsWithAsciiIgnoreCase(line, "Transfer-Encoding:"u8)) {
				return null;
			}

			scanIndex = lineEnd + 2;
		}

		return 0;
	}

	private static int ParsePort(string entry) {
		int separatorIndex = entry.LastIndexOf(':');

		if ((separatorIndex <= 0) || (separatorIndex == (entry.Length - 1))) {
			throw new InvalidOperationException($"Invalid backend endpoint '{entry}'. Expected host:port.");
		}

		return int.Parse(entry[(separatorIndex + 1)..], System.Globalization.CultureInfo.InvariantCulture);
	}

	private static BackendEndpoint[] ParseBackends(string? backendEndpoints) {
		string endpoints = string.IsNullOrWhiteSpace(backendEndpoints)
			? "api1:9999;api2:9999"
			: backendEndpoints;
		string[] entries = endpoints.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		if (entries.Length == 0) {
			throw new InvalidOperationException("At least one backend endpoint must be configured.");
		}

		BackendEndpoint[] backends = new BackendEndpoint[entries.Length];

		for (int index = 0; index < entries.Length; index++) {
			string entry = entries[index];

			if (entry.StartsWith("unix:", StringComparison.OrdinalIgnoreCase)) {
				string socketPath = entry[5..];

				if (string.IsNullOrWhiteSpace(socketPath)) {
					throw new InvalidOperationException($"Invalid unix backend endpoint '{entry}'. Expected unix:/path/to/socket.");
				}

				backends[index] = BackendEndpoint.ForUnixSocket(socketPath);
				continue;
			}

			int separatorIndex = entry.LastIndexOf(':');

			if ((separatorIndex <= 0) || (separatorIndex == (entry.Length - 1))) {
				throw new InvalidOperationException($"Invalid backend endpoint '{entry}'. Expected host:port.");
			}

			backends[index] = BackendEndpoint.ForTcp(entry[..separatorIndex], ParsePort(entry));
		}

		return backends;
	}

	private static int? ParsePositiveInt32(ReadOnlySpan<byte> value) {
		if (value.IsEmpty) {
			return null;
		}

		int parsedValue = 0;

		for (int index = 0; index < value.Length; index++) {
			int digit = value[index] - '0';

			if ((uint)digit > 9u) {
				return null;
			}

			parsedValue = checked((parsedValue * 10) + digit);
		}

		return parsedValue;
	}

	private static int ResolveListenPort(string? urls) {
		if (string.IsNullOrWhiteSpace(urls)) {
			return 9999;
		}

		string[] entries = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		foreach (string entry in entries) {
			int schemeSeparator = entry.IndexOf("://", StringComparison.Ordinal);
			ReadOnlySpan<char> portSpan = (schemeSeparator >= 0)
				? entry.AsSpan(schemeSeparator + 3)
				: entry.AsSpan();
			int colonIndex = portSpan.LastIndexOf(':');

			if (colonIndex < 0) {
				continue;
			}

			portSpan = portSpan[(colonIndex + 1)..];
			int slashIndex = portSpan.IndexOf('/');

			if (slashIndex >= 0) {
				portSpan = portSpan[..slashIndex];
			}

			if (int.TryParse(portSpan, out int port)) {
				return port;
			}
		}

		return 9999;
	}

	private static async Task SendAllAsync(Socket socket, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) {
		int sent = 0;

		while (sent < payload.Length) {
			int bytesSent = await socket.SendAsync(payload[sent..], SocketFlags.None, cancellationToken);

			if (bytesSent <= 0) {
				throw new IOException("Socket send returned zero bytes.");
			}

			sent += bytesSent;
		}
	}

	private static void ShiftBufferLeft(byte[] buffer, int bufferedBytes, int consumedBytes) {
		int remainingBytes = bufferedBytes - consumedBytes;

		if (remainingBytes > 0) {
			Buffer.BlockCopy(buffer, consumedBytes, buffer, 0, remainingBytes);
		}
	}

	private static bool StartsWithAsciiIgnoreCase(ReadOnlySpan<byte> line, ReadOnlySpan<byte> prefix) {
		if (line.Length < prefix.Length) {
			return false;
		}

		for (int index = 0; index < prefix.Length; index++) {
			byte value = line[index];
			byte expected = prefix[index];

			if ((value >= (byte)'A') && (value <= (byte)'Z')) {
				value = (byte)(value + 32);
			}

			if ((expected >= (byte)'A') && (expected <= (byte)'Z')) {
				expected = (byte)(expected + 32);
			}

			if (value != expected) {
				return false;
			}
		}

		return true;
	}

	private static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> value) {
		int start = 0;
		int end = value.Length - 1;

		while ((start < value.Length) && IsAsciiWhitespace(value[start])) {
			start++;
		}

		while ((end >= start) && IsAsciiWhitespace(value[end])) {
			end--;
		}

		return (start <= end)
			? value[start..(end + 1)]
			: ReadOnlySpan<byte>.Empty;
	}

	private static HttpParseStatus TryParseHttpMessage(byte[] buffer, int bufferedBytes, out int consumedBytes) {
		consumedBytes = 0;
		ReadOnlySpan<byte> visibleBuffer = buffer.AsSpan(0, bufferedBytes);
		int headerEnd = FindHeaderTerminator(visibleBuffer);

		if (headerEnd < 0) {
			return HttpParseStatus.Incomplete;
		}

		if (FindCrlf(visibleBuffer, 0) <= 0) {
			return HttpParseStatus.Invalid;
		}

		int headerLength = headerEnd + 4;
		int? contentLength = ParseContentLength(visibleBuffer[..headerEnd]);

		if (contentLength is null) {
			return HttpParseStatus.Invalid;
		}

		int totalLength = checked(headerLength + contentLength.Value);

		if (bufferedBytes < totalLength) {
			return HttpParseStatus.Incomplete;
		}

		consumedBytes = totalLength;
		return HttpParseStatus.Success;
	}

	private static bool IsAsciiWhitespace(byte value) => (value == (byte)' ') || (value == (byte)'\t');

	private enum HttpParseStatus {
		Incomplete = 0,
		Success = 1,
		Invalid = 2,
	}

	private readonly record struct BackendEndpoint(BackendEndpointKind Kind, string Host, int Port, string SocketPath) {
		public static BackendEndpoint ForTcp(string host, int port) => new(BackendEndpointKind.Tcp, host, port, string.Empty);

		public static BackendEndpoint ForUnixSocket(string socketPath) => new(BackendEndpointKind.UnixDomainSocket, string.Empty, 0, socketPath);
	}

	private enum BackendEndpointKind {
		Tcp = 0,
		UnixDomainSocket = 1,
	}

	private sealed class BackendConnection : IDisposable {
		private BackendConnection(Socket socket) {
			this.Socket = socket;
			this.Buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
		}

		public Socket Socket { get; }

		public byte[] Buffer { get; }

		public int BufferedBytes { get; set; }

		public static async Task<BackendConnection> ConnectAsync(BackendEndpoint backend, CancellationToken cancellationToken) {
			Socket socket = backend.Kind switch {
				BackendEndpointKind.Tcp => new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) {
					NoDelay = true,
				},
				BackendEndpointKind.UnixDomainSocket => new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified),
				_ => throw new NotSupportedException($"Unsupported backend endpoint kind '{backend.Kind}'."),
			};

			if (backend.Kind == BackendEndpointKind.UnixDomainSocket) {
				await socket.ConnectAsync(new UnixDomainSocketEndPoint(backend.SocketPath), cancellationToken);
			} else {
				await socket.ConnectAsync(backend.Host, backend.Port, cancellationToken);
			}

			return new BackendConnection(socket);
		}

		public void Dispose() {
			this.Socket.Dispose();
			ArrayPool<byte>.Shared.Return(this.Buffer);
		}
	}
}
