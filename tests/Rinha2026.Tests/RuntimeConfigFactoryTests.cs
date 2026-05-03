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
		Assert.Equal(6, runtimeConfig.Search.BeamLevel1);
		Assert.Equal(12, runtimeConfig.Search.BeamLevel2);
		Assert.Equal(6, runtimeConfig.Detection.ResponseCount);
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
}
