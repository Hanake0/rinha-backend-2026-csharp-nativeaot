using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Rinha2026.Core.Configuration;

namespace Rinha2026.Tests.Support;

internal static class TestApiFactory {
	public static WebApplicationFactory<Program> Create(
		TemporaryRuntimeData runtimeData,
		IndexKind indexKind = IndexKind.HierarchicalBeamIvf,
		ParserMode parserMode = ParserMode.Manual) {
		RuntimeConfig runtimeConfig = new(
			new RuntimeDetectionConfig(TopK: 5, ApprovalThreshold: 0.6d),
			new RuntimeDatasetConfig(
				runtimeData.IndexDirectory,
				runtimeData.MccRiskPath,
				runtimeData.NormalizationPath),
			new RuntimeSearchConfig(
				BeamLevel1: 6,
				BeamLevel2: 12,
				Dimension: 14,
				DistanceMetric: DistanceMetric.SquaredL2,
				IndexKind: indexKind,
				PaddedDimension: 16,
				RerankCount: 48,
				UseLeafRadiusPruning: false,
				UseLastTransactionPartitionPruning: true),
			new RuntimeHttpConfig(
				ParserMode: parserMode,
				ResponseMode: ResponseMode.PrecomputedTable,
				TransportMode: TransportMode.Tcp));

		return new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
			builder.UseEnvironment("Testing");
			builder.ConfigureServices(services => {
				services.RemoveAll(typeof(RuntimeConfig));
				services.AddSingleton(typeof(RuntimeConfig), runtimeConfig);
			});
		});
	}
}
