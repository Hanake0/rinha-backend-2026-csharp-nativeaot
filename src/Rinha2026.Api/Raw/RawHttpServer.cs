using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using Rinha2026.Api.Services;
using Rinha2026.Core.Configuration;

namespace Rinha2026.Api.Raw;

public sealed class RawHttpServer : IAsyncDisposable {
	private const int ReceiveBufferSize = 16 * 1024;
	private const int MaxHeaderBytes = 8 * 1024;

	private readonly ConcurrentDictionary<int, Task> connectionTasks = new();
	private readonly FraudDetectionService detectionService;
	private readonly Socket listener;
	private readonly int listenPort;
	private readonly RequestProfileCollector requestProfileCollector;
	private readonly byte[] badRequestResponse;
	private readonly byte[] notFoundResponse;
	private readonly byte[] okEmptyResponse;
	private readonly byte[] readyResponse;
	private readonly byte[] serviceUnavailableResponse;
	private readonly byte[][] fraudResponses;
	private CancellationTokenSource? cancellationTokenSource;
	private Task? acceptLoopTask;
	private int nextConnectionId;
	private bool disposed;

	private RawHttpServer(
		RuntimeConfig runtimeConfig,
		RawListenOptions listenOptions,
		RequestProfileCollector requestProfileCollector) {
		this.requestProfileCollector = requestProfileCollector;
		this.detectionService = FraudDetectionService.Create(runtimeConfig);
		this.listener = CreateListener(listenOptions);
		this.listener.Listen(backlog: 512);
		this.listenPort = GetListenPort(this.listener);
		this.readyResponse = BuildResponse("200 OK", ReadOnlySpan<byte>.Empty);
		this.serviceUnavailableResponse = BuildResponse("503 Service Unavailable", ReadOnlySpan<byte>.Empty);
		this.badRequestResponse = BuildResponse("400 Bad Request", ReadOnlySpan<byte>.Empty);
		this.notFoundResponse = BuildResponse("404 Not Found", ReadOnlySpan<byte>.Empty);
		this.okEmptyResponse = BuildResponse("200 OK", ReadOnlySpan<byte>.Empty);
		this.fraudResponses = BuildFraudResponses(runtimeConfig);
	}

	public int ListenPort => this.listenPort;

	public static RawHttpServer Create(
		RuntimeConfig runtimeConfig,
		string? urls,
		RequestProfileCollector requestProfileCollector) {
		ArgumentNullException.ThrowIfNull(requestProfileCollector);
		return new RawHttpServer(runtimeConfig, ResolveListenOptions(runtimeConfig.Http, urls), requestProfileCollector);
	}

	public async ValueTask DisposeAsync() {
		if (this.disposed) {
			return;
		}

		await this.StopAsync();
		this.listener.Dispose();
		this.detectionService.Dispose();
		this.disposed = true;
	}

	public async Task RunAsync(CancellationToken cancellationToken = default) {
		await this.StartAsync(cancellationToken);
		Task acceptLoopTask = this.acceptLoopTask ?? Task.CompletedTask;
		await acceptLoopTask;
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

				if (socket.AddressFamily == AddressFamily.InterNetwork) {
					socket.NoDelay = true;
				}

				int connectionId = Interlocked.Increment(ref this.nextConnectionId);
				Task connectionTask = this.HandleConnectionSafelyAsync(connectionId, socket, cancellationToken);
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

	private async Task HandleConnectionSafelyAsync(int connectionId, Socket socket, CancellationToken cancellationToken) {
		try {
			await this.HandleConnectionAsync(socket, cancellationToken);
		} finally {
			this.connectionTasks.TryRemove(connectionId, out _);
			socket.Dispose();
		}
	}

	private async Task HandleConnectionAsync(Socket socket, CancellationToken cancellationToken) {
		byte[] buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
		int bufferedBytes = 0;
		long requestStartTicks = 0L;
		bool requestInFlight = false;

		try {
			while (!cancellationToken.IsCancellationRequested) {
				if (!requestInFlight) {
					requestStartTicks = Stopwatch.GetTimestamp();
					requestInFlight = true;
				}

				int bytesRead = await socket.ReceiveAsync(
					buffer.AsMemory(bufferedBytes),
					SocketFlags.None,
					cancellationToken);

				if (bytesRead <= 0) {
					break;
				}

				bufferedBytes += bytesRead;

				while (bufferedBytes > 0) {
					RequestParseStatus parseStatus = TryParseRequest(
						buffer,
						bufferedBytes,
						out RawHttpRequest request,
						out int consumedBytes);

					if (parseStatus == RequestParseStatus.Incomplete) {
						if (bufferedBytes >= MaxHeaderBytes) {
							await SendAllAsync(socket, this.badRequestResponse, cancellationToken);
							return;
						}

						break;
					}

					if (parseStatus == RequestParseStatus.Invalid) {
						await SendAllAsync(socket, this.badRequestResponse, cancellationToken);
						return;
					}

					await this.HandleRequestAsync(socket, request, requestStartTicks, cancellationToken);
					ShiftBufferLeft(buffer, bufferedBytes, consumedBytes);
					bufferedBytes -= consumedBytes;
					requestStartTicks = Stopwatch.GetTimestamp();
					requestInFlight = bufferedBytes > 0;
				}
			}
		} finally {
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private async Task HandleRequestAsync(
		Socket socket,
		RawHttpRequest request,
		long requestStartTicks,
		CancellationToken cancellationToken) {
		switch (request.Kind) {
			case RawHttpRequestKind.Ready:
				await SendAllAsync(socket, this.readyResponse, cancellationToken);
				return;
			case RawHttpRequestKind.Profile when this.requestProfileCollector.IsEnabled:
				byte[] profileResponse = BuildResponse(
					"200 OK",
					JsonSerializer.SerializeToUtf8Bytes(
						this.requestProfileCollector.Snapshot(),
						ApiJsonContext.Default.RequestProfileSnapshot));
				await SendAllAsync(socket, profileResponse, cancellationToken);
				return;
			case RawHttpRequestKind.ProfileReset when this.requestProfileCollector.IsEnabled:
				this.requestProfileCollector.Reset();
				await SendAllAsync(socket, this.okEmptyResponse, cancellationToken);
				return;
			case RawHttpRequestKind.Profile:
			case RawHttpRequestKind.ProfileReset:
				await SendAllAsync(socket, this.notFoundResponse, cancellationToken);
				return;
			case RawHttpRequestKind.FraudScore:
				await this.HandleFraudScoreAsync(socket, request, requestStartTicks, cancellationToken);
				return;
			default:
				await SendAllAsync(socket, this.notFoundResponse, cancellationToken);
				return;
		}
	}

	private async Task HandleFraudScoreAsync(
		Socket socket,
		RawHttpRequest request,
		long requestStartTicks,
		CancellationToken cancellationToken) {
		if (!this.requestProfileCollector.IsEnabled) {
			if (!this.detectionService.TryHandle(request.Body.Span, out int fraudCount, out ReadOnlyMemory<byte> _)) {
				await SendAllAsync(socket, this.badRequestResponse, cancellationToken);
				return;
			}

			await SendAllAsync(socket, this.fraudResponses[fraudCount], cancellationToken);
			return;
		}

		if (!this.detectionService.TryHandle(
			request.Body.Span,
			out int profiledFraudCount,
			out ReadOnlyMemory<byte> _,
			out FraudDetectionProfile detectionProfile)) {
			await SendAllAsync(socket, this.badRequestResponse, cancellationToken);
			return;
		}

		long beforeWriteTicks = Stopwatch.GetTimestamp();
		await SendAllAsync(socket, this.fraudResponses[profiledFraudCount], cancellationToken);
		long afterWriteTicks = Stopwatch.GetTimestamp();
		this.requestProfileCollector.Record(
			bodyReadStageTicks: detectionProfile.ParseTicks == 0L ? 0L : beforeWriteTicks - requestStartTicks - detectionProfile.ParseTicks - detectionProfile.VectorizeTicks - detectionProfile.SearchTicks,
			parseStageTicks: detectionProfile.ParseTicks,
			vectorizeStageTicks: detectionProfile.VectorizeTicks,
			searchStageTicks: detectionProfile.SearchTicks,
			responseWriteStageTicks: afterWriteTicks - beforeWriteTicks,
			totalStageTicks: afterWriteTicks - requestStartTicks);
	}

	private static byte[] BuildResponse(string status, ReadOnlySpan<byte> payload) {
		string header = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: keep-alive\r\n\r\n";
		byte[] headerBytes = Encoding.ASCII.GetBytes(header);
		byte[] response = new byte[headerBytes.Length + payload.Length];
		Buffer.BlockCopy(headerBytes, 0, response, 0, headerBytes.Length);
		payload.CopyTo(response.AsSpan(headerBytes.Length));
		return response;
	}

	private static byte[][] BuildFraudResponses(RuntimeConfig runtimeConfig) {
		FraudResponseCache payloadCache = new(runtimeConfig.Detection.TopK, runtimeConfig.Detection.ApprovalThreshold);
		byte[][] responses = new byte[runtimeConfig.Detection.TopK + 1][];

		for (int fraudCount = 0; fraudCount < responses.Length; fraudCount++) {
			responses[fraudCount] = BuildResponse("200 OK", payloadCache.GetResponse(fraudCount).Span);
		}

		return responses;
	}

	private static Socket CreateListener(RawListenOptions listenOptions) {
		if (listenOptions.TransportMode == TransportMode.UnixDomainSocket) {
			ArgumentException.ThrowIfNullOrWhiteSpace(listenOptions.UnixSocketPath);
			PrepareUnixSocketPath(listenOptions.UnixSocketPath);
			Socket listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			listener.Bind(new UnixDomainSocketEndPoint(listenOptions.UnixSocketPath));
			EnsureUnixSocketPermissions(listenOptions.UnixSocketPath);
			return listener;
		}

		Socket tcpListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) {
			NoDelay = true,
		};
		tcpListener.Bind(new IPEndPoint(IPAddress.Any, listenOptions.TcpPort));
		return tcpListener;
	}

	private static void EnsureUnixSocketPermissions(string unixSocketPath) {
		if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) {
			return;
		}

		File.SetUnixFileMode(
			unixSocketPath,
			UnixFileMode.UserRead |
			UnixFileMode.UserWrite |
			UnixFileMode.GroupRead |
			UnixFileMode.GroupWrite |
			UnixFileMode.OtherRead |
			UnixFileMode.OtherWrite);
	}

	private static int GetListenPort(Socket listener) {
		return listener.LocalEndPoint is IPEndPoint endPoint
			? endPoint.Port
			: 0;
	}

	private static void PrepareUnixSocketPath(string unixSocketPath) {
		string? directoryPath = Path.GetDirectoryName(unixSocketPath);

		if (!string.IsNullOrWhiteSpace(directoryPath)) {
			Directory.CreateDirectory(directoryPath);
		}

		if (File.Exists(unixSocketPath)) {
			File.Delete(unixSocketPath);
		}
	}

	private static RawListenOptions ResolveListenOptions(RuntimeHttpConfig httpConfig, string? urls) {
		if (httpConfig.TransportMode == TransportMode.UnixDomainSocket) {
			ArgumentException.ThrowIfNullOrWhiteSpace(httpConfig.UnixSocketPath);
			return new RawListenOptions(httpConfig.TransportMode, TcpPort: 0, httpConfig.UnixSocketPath);
		}

		return new RawListenOptions(TransportMode.Tcp, ResolveListenPort(urls), UnixSocketPath: null);
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

	private static RequestParseStatus TryParseRequest(
		byte[] buffer,
		int bufferedBytes,
		out RawHttpRequest request,
		out int consumedBytes) {
		request = default;
		consumedBytes = 0;
		ReadOnlySpan<byte> visibleBuffer = buffer.AsSpan(0, bufferedBytes);
		int headerEnd = FindHeaderTerminator(visibleBuffer);

		if (headerEnd < 0) {
			return RequestParseStatus.Incomplete;
		}

		int requestLineEnd = FindCrlf(visibleBuffer, 0);

		if (requestLineEnd <= 0) {
			return RequestParseStatus.Invalid;
		}

		ReadOnlySpan<byte> requestLine = visibleBuffer[..requestLineEnd];
		RawHttpRequestKind kind = GetRequestKind(requestLine);

		if (kind == RawHttpRequestKind.Unknown) {
			return RequestParseStatus.Invalid;
		}

		int headerLength = headerEnd + 4;

		if ((kind == RawHttpRequestKind.Ready) || (kind == RawHttpRequestKind.Profile) || (kind == RawHttpRequestKind.ProfileReset)) {
			request = new RawHttpRequest(kind, ReadOnlyMemory<byte>.Empty);
			consumedBytes = headerLength;
			return RequestParseStatus.Success;
		}

		int? contentLength = ParseContentLength(visibleBuffer[..headerEnd]);

		if ((contentLength is null) || (contentLength < 0)) {
			return RequestParseStatus.Invalid;
		}

		int totalLength = checked(headerLength + contentLength.Value);

		if (bufferedBytes < totalLength) {
			return RequestParseStatus.Incomplete;
		}

		request = new RawHttpRequest(
			kind,
			buffer.AsMemory(headerLength, contentLength.Value));
		consumedBytes = totalLength;
		return RequestParseStatus.Success;
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

	private static int FindCrlf(ReadOnlySpan<byte> buffer, int startIndex) {
		for (int index = startIndex + 1; index < buffer.Length; index++) {
			if ((buffer[index - 1] == (byte)'\r') && (buffer[index] == (byte)'\n')) {
				return index - 1;
			}
		}

		return -1;
	}

	private static RawHttpRequestKind GetRequestKind(ReadOnlySpan<byte> requestLine) {
		if (requestLine.SequenceEqual("POST /fraud-score HTTP/1.1"u8)) {
			return RawHttpRequestKind.FraudScore;
		}

		if (requestLine.SequenceEqual("GET /ready HTTP/1.1"u8)) {
			return RawHttpRequestKind.Ready;
		}

		if (requestLine.SequenceEqual("GET /debug/profile HTTP/1.1"u8)) {
			return RawHttpRequestKind.Profile;
		}

		if (requestLine.SequenceEqual("POST /debug/profile/reset HTTP/1.1"u8)) {
			return RawHttpRequestKind.ProfileReset;
		}

		return RawHttpRequestKind.Unknown;
	}

	private static int? ParseContentLength(ReadOnlySpan<byte> headers) {
		int scanIndex = 0;

		while (scanIndex < headers.Length) {
			int lineEnd = FindCrlf(headers, scanIndex);

			if (lineEnd < 0) {
				lineEnd = headers.Length;
			}

			ReadOnlySpan<byte> line = headers[scanIndex..lineEnd];

			if (line.StartsWith("Content-Length:"u8)) {
				ReadOnlySpan<byte> value = TrimAsciiWhitespace(line["Content-Length:".Length..]);
				return ParsePositiveInt32(value);
			}

			scanIndex = lineEnd + 2;
		}

		return null;
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

	private static bool IsAsciiWhitespace(byte value) {
		return (value == (byte)' ') || (value == (byte)'\t');
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
}

internal enum RawHttpRequestKind {
	Unknown = 0,
	FraudScore = 1,
	Ready = 2,
	Profile = 3,
	ProfileReset = 4,
}

internal readonly record struct RawHttpRequest(
	RawHttpRequestKind Kind,
	ReadOnlyMemory<byte> Body);

internal enum RequestParseStatus {
	Incomplete = 0,
	Success = 1,
	Invalid = 2,
}

internal readonly record struct RawListenOptions(
	TransportMode TransportMode,
	int TcpPort,
	string? UnixSocketPath);
