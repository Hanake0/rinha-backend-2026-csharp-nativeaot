using System.Text;

using Rinha2026.Core.Parsing;

namespace Rinha2026.Tests;

public sealed class ReferenceFraudRequestParserTests {
	[Fact]
	public void TryParseParsesKnownMerchantAndLastTransaction() {
		byte[] payload = Encoding.UTF8.GetBytes("""
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

		bool parsed = ReferenceFraudRequestParser.TryParse(payload, out var request);

		Assert.True(parsed);
		Assert.Equal(384.88d, request.Transaction.Amount, 3);
		Assert.Equal(3, request.Transaction.Installments);
		Assert.Equal(20, request.Transaction.RequestedAt.Hour);
		Assert.Equal(1, request.Merchant.Id);
		Assert.Equal(5912, request.Merchant.Mcc);
		Assert.True(request.LastTransaction.HasValue);
		Assert.True(request.Customer.KnowsMerchant(1));
		Assert.Equal(3, request.Customer.KnownMerchants.Count);
	}

	[Fact]
	public void TryParseParsesNullLastTransaction() {
		byte[] payload = Encoding.UTF8.GetBytes("""
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

		bool parsed = ReferenceFraudRequestParser.TryParse(payload, out var request);

		Assert.True(parsed);
		Assert.False(request.LastTransaction.HasValue);
		Assert.Equal(16, request.Merchant.Id);
		Assert.True(request.Customer.KnowsMerchant(16));
	}
}
