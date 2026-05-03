using System.Text;

using Rinha2026.Api.Raw;
using Rinha2026.Api.Services;
using Rinha2026.Core.Configuration;
using Rinha2026.Tests.Support;

namespace Rinha2026.Tests;

public sealed class RawHttpServerTests {
	[Fact]
	public async Task RawHttpServerServesReadyAndFraudScore() {
		using TemporaryRuntimeData runtimeData = await TemporaryRuntimeData.CreateAsync(
			([0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f], "legit"),
			([1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f], "fraud"));
		RuntimeConfig runtimeConfig = CreateRuntimeConfig(runtimeData);
		RequestProfileCollector requestProfileCollector = new(runtimeConfig.Diagnostics);
		await using RawHttpServer server = RawHttpServer.Create(runtimeConfig, "http://127.0.0.1:0", requestProfileCollector);
		await server.StartAsync();

		try {
			using HttpClient client = new() {
				BaseAddress = new Uri($"http://127.0.0.1:{server.ListenPort}", UriKind.Absolute),
			};
			using HttpResponseMessage readyResponse = await client.GetAsync("/ready");

			Assert.True(readyResponse.IsSuccessStatusCode);

			byte[] payload = Encoding.UTF8.GetBytes(
				"""
				{
				  "transaction": {
				    "amount": 100.0,
				    "installments": 1,
				    "requested_at": "2026-01-01T10:00:00Z"
				  },
				  "customer": {
				    "avg_amount": 90.0,
				    "known_merchants": ["MERC-0001"],
				    "tx_count_24h": 2
				  },
				  "merchant": {
				    "avg_amount": 100.0,
				    "id": "MERC-0001",
				    "mcc": "5411"
				  },
				  "terminal": {
				    "card_present": true,
				    "is_online": false,
				    "km_from_home": 1.0
				  },
				  "last_transaction": null
				}
				""");
			using FraudDetectionService service = FraudDetectionService.Create(runtimeConfig);
			Assert.True(service.TryHandle(payload, out ReadOnlyMemory<byte> expectedResponse));
			using ByteArrayContent content = new(payload);
			content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
			using HttpResponseMessage fraudScoreResponse = await client.PostAsync("/fraud-score", content);
			byte[] actualResponse = await fraudScoreResponse.Content.ReadAsByteArrayAsync();

			Assert.True(fraudScoreResponse.IsSuccessStatusCode);
			Assert.Equal(expectedResponse.ToArray(), actualResponse);
		} finally {
			await server.StopAsync();
		}
	}

	private static RuntimeConfig CreateRuntimeConfig(TemporaryRuntimeData runtimeData) {
		return new RuntimeConfig(
			new RuntimeDetectionConfig(TopK: 1, ApprovalThreshold: 0.6d),
			new RuntimeDatasetConfig(
				runtimeData.IndexDirectory,
				runtimeData.MccRiskPath,
				runtimeData.NormalizationPath),
			new RuntimeSearchConfig(
				BeamLevel1: 1,
				BeamLevel2: 1,
				Dimension: 14,
				DistanceMetric: DistanceMetric.SquaredL2,
				IndexKind: IndexKind.ExactSampleOnly,
				PaddedDimension: 16,
				RerankCount: 1,
				BoundaryRerankCount: 1,
				UseLeafRadiusPruning: false,
				UseLastTransactionPartitionPruning: false),
			new RuntimeHttpConfig(
				ParserMode: ParserMode.Manual,
				ResponseMode: ResponseMode.PrecomputedTable,
				ServerMode: ServerMode.RawSockets,
				TransportMode: TransportMode.Tcp,
				UnixSocketPath: null),
			new RuntimeDiagnosticsConfig(
				ProfileEnabled: false,
				ProfileSampleCapacity: 128,
				ProfileSamplingStride: 1));
	}
}
