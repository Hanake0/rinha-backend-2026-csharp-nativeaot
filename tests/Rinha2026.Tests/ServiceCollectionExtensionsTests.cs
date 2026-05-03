using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Rinha2026.Api.Configuration;
using Rinha2026.Core.Configuration;

namespace Rinha2026.Tests;

public sealed class ServiceCollectionExtensionsTests {
	[Fact]
	public void AddRuntimeServicesReadsNestedConfigurationValues() {
		Dictionary<string, string?> values = new(StringComparer.Ordinal) {
			["Runtime:Detection:TopK"] = "7",
			["Runtime:Detection:ApprovalThreshold"] = "0.75",
			["Runtime:Dataset:IndexDirectory"] = "./custom-index",
			["Runtime:Dataset:MccRiskPath"] = "./custom-mcc.json",
			["Runtime:Dataset:NormalizationPath"] = "./custom-normalization.json",
			["Runtime:Search:BeamLevel1"] = "10",
			["Runtime:Search:BeamLevel2"] = "17",
			["Runtime:Search:Dimension"] = "14",
			["Runtime:Search:DistanceMetric"] = "SquaredL2",
			["Runtime:Search:IndexKind"] = "HierarchicalBeamIvf",
			["Runtime:Search:PaddedDimension"] = "16",
			["Runtime:Search:RerankCount"] = "68",
			["Runtime:Http:ParserMode"] = "ReferenceStj",
			["Runtime:Http:ResponseMode"] = "PrecomputedTable",
			["Runtime:Http:TransportMode"] = "Tcp",
		};

		IConfiguration configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(values)
			.Build();
		ServiceCollection services = [];

		services.AddRuntimeServices(configuration, "C:\\repo-root");

		using ServiceProvider provider = services.BuildServiceProvider();
		RuntimeConfig runtimeConfig = provider.GetRequiredService<RuntimeConfig>();

		Assert.Equal(7, runtimeConfig.Detection.TopK);
		Assert.Equal(0.75d, runtimeConfig.Detection.ApprovalThreshold);
		Assert.Equal(10, runtimeConfig.Search.BeamLevel1);
		Assert.Equal(17, runtimeConfig.Search.BeamLevel2);
		Assert.Equal(68, runtimeConfig.Search.RerankCount);
		Assert.Equal(ParserMode.ReferenceStj, runtimeConfig.Http.ParserMode);
		Assert.EndsWith("custom-index", runtimeConfig.Dataset.IndexDirectory, StringComparison.Ordinal);
		Assert.EndsWith("custom-mcc.json", runtimeConfig.Dataset.MccRiskPath, StringComparison.Ordinal);
		Assert.EndsWith("custom-normalization.json", runtimeConfig.Dataset.NormalizationPath, StringComparison.Ordinal);
	}
}
