using System.Text;

using BenchmarkDotNet.Attributes;

using Rinha2026.Api.Services;
using Rinha2026.Benchmarks.Support;
using Rinha2026.Core.Configuration;

namespace Rinha2026.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class FraudDetectionPipelineBenchmarks {
	private FraudDetectionService approximateService = default!;
	private FraudDetectionService exactService = default!;
	private byte[] payload = default!;
	private TemporaryRuntimeData runtimeData = default!;

	[GlobalSetup]
	public async Task Setup() {
		Random random = new(20260502);
		var records = new (float[] Vector, string Label)[4096];

		for (int index = 0; index < records.Length; index++) {
			float[] vector = new float[14];
			float baseValue = (index % 8) / 7f;

			for (int dimension = 0; dimension < vector.Length; dimension++) {
				float jitter = (float)random.NextDouble() * 0.02f;
				vector[dimension] = Math.Clamp(baseValue + jitter, 0f, 1f);
			}

			records[index] = (vector, (index & 1) == 0 ? "legit" : "fraud");
		}

		this.runtimeData = await TemporaryRuntimeData.CreateAsync(records);
		this.exactService = FraudDetectionService.Create(this.CreateRuntimeConfig(IndexKind.ExactSampleOnly));
		this.approximateService = FraudDetectionService.Create(this.CreateRuntimeConfig(IndexKind.HierarchicalBeamIvf));
		this.payload = Encoding.UTF8.GetBytes("""
		{
		  "id": "tx-1329056812",
		  "transaction": {
		    "amount": 41.12,
		    "installments": 2,
		    "requested_at": "2026-03-11T18:45:53Z"
		  },
		  "customer": {
		    "avg_amount": 82.24,
		    "tx_count_24h": 3,
		    "known_merchants": ["MERC-003", "MERC-016"]
		  },
		  "merchant": {
		    "id": "MERC-016",
		    "mcc": "5411",
		    "avg_amount": 60.25
		  },
		  "terminal": {
		    "is_online": false,
		    "card_present": true,
		    "km_from_home": 29.23
		  },
		  "last_transaction": null
		}
		""");
	}

	[GlobalCleanup]
	public void Cleanup() {
		this.approximateService.Dispose();
		this.exactService.Dispose();
		this.runtimeData.Dispose();
	}

	[Benchmark(Baseline = true)]
	public int HandleExact() {
		this.exactService.TryHandle(this.payload, out ReadOnlyMemory<byte> response);
		return response.Length;
	}

	[Benchmark]
	public int HandleApproximate() {
		this.approximateService.TryHandle(this.payload, out ReadOnlyMemory<byte> response);
		return response.Length;
	}

	private RuntimeConfig CreateRuntimeConfig(IndexKind indexKind) => new(
		new RuntimeDetectionConfig(TopK: 5, ApprovalThreshold: 0.6d),
		new RuntimeDatasetConfig(
			this.runtimeData.IndexDirectory,
			this.runtimeData.MccRiskPath,
			this.runtimeData.NormalizationPath),
		new RuntimeSearchConfig(
			BeamLevel1: 2,
			BeamLevel2: 8,
			Dimension: 14,
			DistanceMetric: DistanceMetric.SquaredL2,
			IndexKind: indexKind,
			PaddedDimension: 16,
			RerankCount: 48,
			UseLeafRadiusPruning: false,
			UseLastTransactionPartitionPruning: true),
		new RuntimeHttpConfig(
			ParserMode: ParserMode.Manual,
			ResponseMode: ResponseMode.PrecomputedTable,
			TransportMode: TransportMode.Tcp));
}
