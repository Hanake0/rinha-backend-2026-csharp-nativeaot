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
			["Runtime:Search:BoundaryRerankCount"] = "96",
			["Runtime:Search:UseLeafRadiusPruning"] = "true",
			["Runtime:Search:UseLastTransactionPartitionPruning"] = "false",
			["Runtime:Http:ParserMode"] = "ReferenceStj",
			["Runtime:Http:ResponseMode"] = "PrecomputedTable",
			["Runtime:Http:TransportMode"] = "UnixDomainSocket",
			["Runtime:Http:UnixSocketPath"] = "./sockets/api1.sock",
			["Runtime:Diagnostics:ProfileEnabled"] = "true",
			["Runtime:Diagnostics:ProfileSampleCapacity"] = "2048",
			["Runtime:Diagnostics:ProfileSamplingStride"] = "32",
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
		Assert.Equal(96, runtimeConfig.Search.BoundaryRerankCount);
		Assert.True(runtimeConfig.Search.UseLeafRadiusPruning);
		Assert.False(runtimeConfig.Search.UseLastTransactionPartitionPruning);
		Assert.Equal(ParserMode.ReferenceStj, runtimeConfig.Http.ParserMode);
		Assert.Equal(TransportMode.UnixDomainSocket, runtimeConfig.Http.TransportMode);
		Assert.EndsWith("sockets" + Path.DirectorySeparatorChar + "api1.sock", runtimeConfig.Http.UnixSocketPath, StringComparison.Ordinal);
		Assert.True(runtimeConfig.Diagnostics.ProfileEnabled);
		Assert.Equal(2048, runtimeConfig.Diagnostics.ProfileSampleCapacity);
		Assert.Equal(32, runtimeConfig.Diagnostics.ProfileSamplingStride);
		Assert.EndsWith("custom-index", runtimeConfig.Dataset.IndexDirectory, StringComparison.Ordinal);
		Assert.EndsWith("custom-mcc.json", runtimeConfig.Dataset.MccRiskPath, StringComparison.Ordinal);
		Assert.EndsWith("custom-normalization.json", runtimeConfig.Dataset.NormalizationPath, StringComparison.Ordinal);
	}
}
