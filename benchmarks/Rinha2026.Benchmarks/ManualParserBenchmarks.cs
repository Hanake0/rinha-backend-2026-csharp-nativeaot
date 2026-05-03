using System.Text;

using BenchmarkDotNet.Attributes;

using Rinha2026.Core.Parsing;

namespace Rinha2026.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class ManualParserBenchmarks {
	private byte[] payloadWithLastTransaction = default!;
	private byte[] payloadWithoutLastTransaction = default!;

	[GlobalSetup]
	public void Setup() {
		this.payloadWithLastTransaction = Encoding.UTF8.GetBytes("""
		{
		  "id": "tx-3576980410",
		  "transaction": {
		    "amount": 384.88,
		    "installments": 3,
		    "requested_at": "2026-03-11T20:23:35Z"
		  },
		  "customer": {
		    "avg_amount": 769.76,
		    "tx_count_24h": 3,
		    "known_merchants": ["MERC-009", "MERC-001", "MERC-001"]
		  },
		  "merchant": {
		    "id": "MERC-001",
		    "mcc": "5912",
		    "avg_amount": 298.95
		  },
		  "terminal": {
		    "is_online": false,
		    "card_present": true,
		    "km_from_home": 13.7090520965
		  },
		  "last_transaction": {
		    "timestamp": "2026-03-11T14:58:35Z",
		    "km_from_current": 18.8626479774
		  }
		}
		""");

		this.payloadWithoutLastTransaction = Encoding.UTF8.GetBytes("""
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

	[Benchmark]
	public int ParseWithLastTransaction() {
		ManualFraudRequestParser.TryParse(this.payloadWithLastTransaction, out var request);
		return request.Merchant.Mcc;
	}

	[Benchmark]
	public int ParseWithoutLastTransaction() {
		ManualFraudRequestParser.TryParse(this.payloadWithoutLastTransaction, out var request);
		return request.Merchant.Mcc;
	}
}
