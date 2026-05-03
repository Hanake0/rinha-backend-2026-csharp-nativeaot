using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;

namespace Rinha2026.Api.Configuration;

public static class SocketTransportConfiguration {
	public static void Configure(IWebHostBuilder webHostBuilder, IConfiguration configuration) {
		ArgumentNullException.ThrowIfNull(webHostBuilder);
		ArgumentNullException.ThrowIfNull(configuration);

		if (!TryReadOptions(configuration, out SocketTransportSettings settings)) {
			return;
		}

		webHostBuilder.UseSockets(options => Apply(options, settings));
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
			TryGetInt32(configuration, "Runtime:Http:IoQueueCount"),
			TryGetBoolean(configuration, "Runtime:Http:NoDelay"),
			TryGetBoolean(configuration, "Runtime:Http:UnsafePreferInlineScheduling"));

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
}

public readonly record struct SocketTransportSettings(
	int? IoQueueCount,
	bool? NoDelay,
	bool? UnsafePreferInlineScheduling) {
	public bool HasOverrides =>
		this.IoQueueCount is not null ||
		this.NoDelay is not null ||
		this.UnsafePreferInlineScheduling is not null;
}
