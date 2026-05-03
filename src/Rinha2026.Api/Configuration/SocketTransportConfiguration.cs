using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;

using Rinha2026.Core.Configuration;

namespace Rinha2026.Api.Configuration;

public static class SocketTransportConfiguration {
	public static void Configure(IWebHostBuilder webHostBuilder, IConfiguration configuration) {
		ArgumentNullException.ThrowIfNull(webHostBuilder);
		ArgumentNullException.ThrowIfNull(configuration);

		if (!TryReadOptions(configuration, out SocketTransportSettings settings)) {
			return;
		}

		webHostBuilder.UseSockets(options => Apply(options, settings));

		if (settings.TransportMode == TransportMode.UnixDomainSocket) {
			ArgumentException.ThrowIfNullOrWhiteSpace(settings.UnixSocketPath);
			PrepareUnixSocketPath(settings.UnixSocketPath);

			webHostBuilder.ConfigureKestrel(serverOptions => {
				serverOptions.ListenUnixSocket(settings.UnixSocketPath, listenOptions => {
					listenOptions.Protocols = HttpProtocols.Http1;
				});
			});
		}
	}

	public static void ConfigureApplication(IHostApplicationLifetime applicationLifetime, RuntimeHttpConfig httpConfig) {
		ArgumentNullException.ThrowIfNull(applicationLifetime);

		if ((httpConfig.TransportMode != TransportMode.UnixDomainSocket) ||
			string.IsNullOrWhiteSpace(httpConfig.UnixSocketPath)) {
			return;
		}

		applicationLifetime.ApplicationStarted.Register(() => EnsureUnixSocketPermissions(httpConfig.UnixSocketPath));
	}

	public static void Apply(SocketTransportOptions options, SocketTransportSettings settings) {
		ArgumentNullException.ThrowIfNull(options);

		if (settings.IoQueueCount is int ioQueueCount) {
			options.IOQueueCount = ioQueueCount;
		}

		if (settings.NoDelay is bool noDelay) {
			options.NoDelay = noDelay;
		}

		if (settings.UnsafePreferInlineScheduling is bool unsafePreferInlineScheduling) {
			options.UnsafePreferInlineScheduling = unsafePreferInlineScheduling;
		}
	}

	public static bool TryReadOptions(IConfiguration configuration, out SocketTransportSettings settings) {
		ArgumentNullException.ThrowIfNull(configuration);

		settings = new SocketTransportSettings(
			TryGetEnum<TransportMode>(configuration, "Runtime:Http:TransportMode"),
			TryGetInt32(configuration, "Runtime:Http:IoQueueCount"),
			TryGetBoolean(configuration, "Runtime:Http:NoDelay"),
			TryGetBoolean(configuration, "Runtime:Http:UnsafePreferInlineScheduling"),
			TryGetString(configuration, "Runtime:Http:UnixSocketPath"));

		return settings.HasOverrides;
	}

	private static bool? TryGetBoolean(IConfiguration configuration, string key) {
		string? value = configuration[key];
		return bool.TryParse(value, out bool parsedValue) ? parsedValue : null;
	}

	private static int? TryGetInt32(IConfiguration configuration, string key) {
		string? value = configuration[key];
		return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsedValue)
			? parsedValue
			: null;
	}

	private static TEnum? TryGetEnum<TEnum>(IConfiguration configuration, string key) where TEnum : struct {
		string? value = configuration[key];
		return Enum.TryParse<TEnum>(value, ignoreCase: true, out TEnum parsedValue) ? parsedValue : null;
	}

	private static string? TryGetString(IConfiguration configuration, string key) {
		string? value = configuration[key];
		return string.IsNullOrWhiteSpace(value) ? null : value;
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

	private static void EnsureUnixSocketPermissions(string unixSocketPath) {
		if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) {
			return;
		}

		for (int attempt = 0; attempt < 200; attempt++) {
			if (File.Exists(unixSocketPath)) {
				File.SetUnixFileMode(
					unixSocketPath,
					UnixFileMode.UserRead |
					UnixFileMode.UserWrite |
					UnixFileMode.GroupRead |
					UnixFileMode.GroupWrite |
					UnixFileMode.OtherRead |
					UnixFileMode.OtherWrite);

				return;
			}

			Thread.Sleep(25);
		}
	}
}

public readonly record struct SocketTransportSettings(
	TransportMode? TransportMode,
	int? IoQueueCount,
	bool? NoDelay,
	bool? UnsafePreferInlineScheduling,
	string? UnixSocketPath) {
	public bool HasOverrides =>
		this.TransportMode is not null ||
		this.IoQueueCount is not null ||
		this.NoDelay is not null ||
		this.UnsafePreferInlineScheduling is not null ||
		!string.IsNullOrWhiteSpace(this.UnixSocketPath);
}
