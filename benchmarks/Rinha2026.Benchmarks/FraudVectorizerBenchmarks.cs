using System.Text;

using BenchmarkDotNet.Attributes;

using Rinha2026.Core.Detection;
using Rinha2026.Core.Parsing;
using Rinha2026.Core.Vectorization;

namespace Rinha2026.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class FraudVectorizerBenchmarks {
	private static readonly MccRiskTable DefaultRiskTable = MccRiskTable.CreateDefault();

	private Rinha2026.Core.Model.FraudRequest fraudRequest;
	private Rinha2026.Core.Model.FraudRequest legitRequest;
	private float[] vector = default!;

	[GlobalSetup]
	public void Setup() {
		byte[] legitPayload = Encoding.UTF8.GetBytes("""
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

		byte[] fraudPayload = Encoding.UTF8.GetBytes("""
		{
		  "id": "tx-3330991687",
		  "transaction": {
		    "amount": 9505.97,
		    "installments": 10,
		    "requested_at": "2026-03-14T05:15:12Z"
		  },
		  "customer": {
		    "avg_amount": 81.28,
		    "tx_count_24h": 20,
		    "known_merchants": ["MERC-008", "MERC-007", "MERC-005"]
		  },
		  "merchant": {
		    "id": "MERC-068",
		    "mcc": "7802",
		    "avg_amount": 54.86
		  },
		  "terminal": {
		    "is_online": false,
		    "card_present": true,
		    "km_from_home": 952.27
		  },
		  "last_transaction": null
		}
		""");

		ReferenceFraudRequestParser.TryParse(legitPayload, out this.legitRequest);
		ReferenceFraudRequestParser.TryParse(fraudPayload, out this.fraudRequest);
		this.vector = new float[FraudVectorizer.PaddedDimension];
	}

	[Benchmark]
	public float VectorizeFraud() {
		FraudVectorizer.WriteVector(this.fraudRequest, NormalizationConstants.Default, DefaultRiskTable, this.vector);
		return this.vector[12];
	}

	[Benchmark]
	public float VectorizeLegit() {
		FraudVectorizer.WriteVector(this.legitRequest, NormalizationConstants.Default, DefaultRiskTable, this.vector);
		return this.vector[12];
	}
}
