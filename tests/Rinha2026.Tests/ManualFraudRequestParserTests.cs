using System.Text;

using Rinha2026.Core.Parsing;

namespace Rinha2026.Tests;

public sealed class ManualFraudRequestParserTests {
	[Fact]
	public void TryParseMatchesReferenceParserForPayloadWithLastTransaction() {
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

		Assert.True(ReferenceFraudRequestParser.TryParse(payload, out var reference));
		Assert.True(ManualFraudRequestParser.TryParse(payload, out var manual));
		AssertRequestsEqual(reference, manual);
	}

	[Fact]
	public void TryParseMatchesReferenceParserForPayloadWithoutLastTransaction() {
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

		Assert.True(ReferenceFraudRequestParser.TryParse(payload, out var reference));
		Assert.True(ManualFraudRequestParser.TryParse(payload, out var manual));
		AssertRequestsEqual(reference, manual);
	}

	[Fact]
	public void TryParseMatchesReferenceParserForOutOfOrderPayloadWithIgnoredFields() {
		byte[] payload = Encoding.UTF8.GetBytes("""
		{
		  "last_transaction": {
		    "ignored": [1, 2, 3],
		    "km_from_current": 18.8626479774,
		    "timestamp": "2026-03-11T14:58:35Z"
		  },
		  "terminal": {
		    "km_from_home": 13.7090520965,
		    "card_present": true,
		    "ignored": {"x": false},
		    "is_online": false
		  },
		  "merchant": {
		    "avg_amount": 298.95,
		    "mcc": "5912",
		    "id": "MERC-001"
		  },
		  "id": "tx-3576980410",
		  "customer": {
		    "known_merchants": ["MERC-009", "MERC-001", "MERC-001"],
		    "tx_count_24h": 3,
		    "avg_amount": 769.76
		  },
		  "transaction": {
		    "requested_at": "2026-03-11T20:23:35Z",
		    "installments": 3,
		    "amount": 384.88
		  }
		}
		""");

		Assert.True(ReferenceFraudRequestParser.TryParse(payload, out var reference));
		Assert.True(ManualFraudRequestParser.TryParse(payload, out var manual));
		AssertRequestsEqual(reference, manual);
	}

	private static void AssertRequestsEqual(
		Rinha2026.Core.Model.FraudRequest expected,
		Rinha2026.Core.Model.FraudRequest actual) {
		Assert.Equal(expected.Transaction.Amount, actual.Transaction.Amount);
		Assert.Equal(expected.Transaction.Installments, actual.Transaction.Installments);
		Assert.Equal(expected.Transaction.RequestedAt, actual.Transaction.RequestedAt);
		Assert.Equal(expected.Customer.AverageAmount, actual.Customer.AverageAmount);
		Assert.Equal(expected.Customer.TransactionCountLast24Hours, actual.Customer.TransactionCountLast24Hours);
		Assert.Equal(expected.Merchant.AverageAmount, actual.Merchant.AverageAmount);
		Assert.Equal(expected.Merchant.Id, actual.Merchant.Id);
		Assert.Equal(expected.Merchant.Mcc, actual.Merchant.Mcc);
		Assert.Equal(expected.Terminal.CardPresent, actual.Terminal.CardPresent);
		Assert.Equal(expected.Terminal.IsOnline, actual.Terminal.IsOnline);
		Assert.Equal(expected.Terminal.KilometersFromHome, actual.Terminal.KilometersFromHome);
		Assert.Equal(expected.LastTransaction.HasValue, actual.LastTransaction.HasValue);
		Assert.Equal(expected.LastTransaction.KilometersFromCurrent, actual.LastTransaction.KilometersFromCurrent);
		Assert.Equal(expected.LastTransaction.Timestamp, actual.LastTransaction.Timestamp);

		for (int merchantId = 0; merchantId <= 32; merchantId++) {
			Assert.Equal(
				expected.Customer.KnowsMerchant(merchantId),
				actual.Customer.KnowsMerchant(merchantId));
		}
	}
}
