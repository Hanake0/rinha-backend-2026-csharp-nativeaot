using Rinha2026.Core.Configuration;

namespace Rinha2026.Tests;

public sealed class RuntimeConfigFactoryTests {
	[Fact]
	public void CreateNormalizesRelativePathsAgainstContentRoot() {
		string contentRootPath = Path.Combine("C:\\", "work", "submission");
		RuntimeSettings settings = new() {
			Dataset = new DatasetSettings {
				IndexDirectory = "./data/index",
				MccRiskPath = "./data/mcc_risk.json",
				NormalizationPath = "./data/normalization.json",
			},
		};

		RuntimeConfig runtimeConfig = RuntimeConfigFactory.Create(settings, contentRootPath);

		Assert.Equal(Path.Combine(contentRootPath, "data", "index"), runtimeConfig.Dataset.IndexDirectory);
		Assert.Equal(Path.Combine(contentRootPath, "data", "mcc_risk.json"), runtimeConfig.Dataset.MccRiskPath);
		Assert.Equal(Path.Combine(contentRootPath, "data", "normalization.json"), runtimeConfig.Dataset.NormalizationPath);
		Assert.Equal(10, runtimeConfig.Search.BeamLevel1);
		Assert.Equal(32, runtimeConfig.Search.BeamLevel2);
		Assert.Equal(48, runtimeConfig.Search.RerankCount);
		Assert.Equal(48, runtimeConfig.Search.BoundaryRerankCount);
		Assert.False(runtimeConfig.Search.UseLeafRadiusPruning);
		Assert.True(runtimeConfig.Search.UseLastTransactionPartitionPruning);
		Assert.Equal(6, runtimeConfig.Detection.ResponseCount);
		Assert.Equal(3, runtimeConfig.Detection.MinDeniedCount);
		Assert.Equal(2, runtimeConfig.Detection.MaxApprovedCount);
		Assert.Equal(ServerMode.Kestrel, runtimeConfig.Http.ServerMode);
		Assert.Null(runtimeConfig.Http.UnixSocketPath);
		Assert.False(runtimeConfig.Diagnostics.ProfileEnabled);
		Assert.Equal(8192, runtimeConfig.Diagnostics.ProfileSampleCapacity);
		Assert.Equal(64, runtimeConfig.Diagnostics.ProfileSamplingStride);
	}

	[Fact]
	public void CreateRejectsInvalidTopK() {
		RuntimeSettings settings = new() {
			Detection = new DetectionSettings {
				TopK = 0,
			},
		};

		Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeConfigFactory.Create(settings, AppContext.BaseDirectory));
	}

	[Fact]
	public void CreateRejectsInvalidThreshold() {
		RuntimeSettings settings = new() {
			Detection = new DetectionSettings {
				ApprovalThreshold = 2.0d,
			},
		};

		Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeConfigFactory.Create(settings, AppContext.BaseDirectory));
	}

	[Fact]
	public void CreateRejectsInvalidPaddedDimension() {
		RuntimeSettings settings = new() {
			Search = new SearchSettings {
				Dimension = 14,
				PaddedDimension = 13,
			},
		};

		Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeConfigFactory.Create(settings, AppContext.BaseDirectory));
	}

	[Fact]
	public void CreateRejectsInvalidProfileSampleCapacity() {
		RuntimeSettings settings = new() {
			Diagnostics = new DiagnosticsSettings {
				ProfileSampleCapacity = 0,
			},
		};

		Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeConfigFactory.Create(settings, AppContext.BaseDirectory));
	}

	[Fact]
	public void CreateRejectsUnixDomainSocketModeWithoutPath() {
		RuntimeSettings settings = new() {
			Http = new HttpSettings {
				TransportMode = TransportMode.UnixDomainSocket,
			},
		};

		Assert.Throws<ArgumentException>(() => RuntimeConfigFactory.Create(settings, AppContext.BaseDirectory));
	}

	[Fact]
	public void CreateRejectsBoundaryRerankCountBelowPrimaryRerankCount() {
		RuntimeSettings settings = new() {
			Search = new SearchSettings {
				RerankCount = 48,
				BoundaryRerankCount = 32,
			},
		};

		Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeConfigFactory.Create(settings, AppContext.BaseDirectory));
	}

	[Fact]
	public void CreateRejectsInvalidProfileSamplingStride() {
		RuntimeSettings settings = new() {
			Diagnostics = new DiagnosticsSettings {
				ProfileSamplingStride = 0,
			},
		};

		Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeConfigFactory.Create(settings, AppContext.BaseDirectory));
	}

	[Fact]
	public void CreateNormalizesUnixSocketPathAgainstContentRoot() {
		string contentRootPath = Path.Combine("C:\\", "work", "submission");
		RuntimeSettings settings = new() {
			Http = new HttpSettings {
				ServerMode = ServerMode.RawSockets,
				TransportMode = TransportMode.UnixDomainSocket,
				UnixSocketPath = "./sockets/api1.sock",
			},
		};

		RuntimeConfig runtimeConfig = RuntimeConfigFactory.Create(settings, contentRootPath);

		Assert.Equal(ServerMode.RawSockets, runtimeConfig.Http.ServerMode);
		Assert.Equal(Path.Combine(contentRootPath, "sockets", "api1.sock"), runtimeConfig.Http.UnixSocketPath);
	}
}
