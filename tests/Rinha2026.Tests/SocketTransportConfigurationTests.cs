using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Configuration;

using Rinha2026.Api.Configuration;

namespace Rinha2026.Tests;

public sealed class SocketTransportConfigurationTests {
	[Fact]
	public void TryReadOptionsReturnsFalseWhenNoOverridesExist() {
		IConfiguration configuration = new ConfigurationBuilder()
			.AddInMemoryCollection([])
			.Build();

		bool hasOverrides = SocketTransportConfiguration.TryReadOptions(configuration, out SocketTransportSettings settings);

		Assert.False(hasOverrides);
		Assert.False(settings.HasOverrides);
	}

	[Fact]
	public void TryReadOptionsReadsRuntimeHttpTransportOverrides() {
		Dictionary<string, string?> values = new(StringComparer.Ordinal) {
			["Runtime:Http:TransportMode"] = "UnixDomainSocket",
			["Runtime:Http:IoQueueCount"] = "0",
			["Runtime:Http:NoDelay"] = "true",
			["Runtime:Http:UnsafePreferInlineScheduling"] = "true",
			["Runtime:Http:UnixSocketPath"] = "/sockets/api1.sock",
		};
		IConfiguration configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(values)
			.Build();

		bool hasOverrides = SocketTransportConfiguration.TryReadOptions(configuration, out SocketTransportSettings settings);

		Assert.True(hasOverrides);
		Assert.True(settings.HasOverrides);
		Assert.Equal(Rinha2026.Core.Configuration.TransportMode.UnixDomainSocket, settings.TransportMode);
		Assert.Equal(0, settings.IoQueueCount);
		Assert.True(settings.NoDelay);
		Assert.True(settings.UnsafePreferInlineScheduling);
		Assert.Equal("/sockets/api1.sock", settings.UnixSocketPath);
	}

	[Fact]
	public void ApplyOverridesConfiguredSocketOptions() {
		SocketTransportOptions options = new() {
			IOQueueCount = 7,
			NoDelay = false,
			UnsafePreferInlineScheduling = false,
		};

		SocketTransportConfiguration.Apply(
			options,
			new SocketTransportSettings(
				TransportMode: null,
				IoQueueCount: 0,
				NoDelay: true,
				UnsafePreferInlineScheduling: true,
				UnixSocketPath: null));

		Assert.Equal(0, options.IOQueueCount);
		Assert.True(options.NoDelay);
		Assert.True(options.UnsafePreferInlineScheduling);
	}
}
