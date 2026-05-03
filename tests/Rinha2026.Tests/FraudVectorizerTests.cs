using System.Text;

using Rinha2026.Core.Detection;
using Rinha2026.Core.Parsing;
using Rinha2026.Core.Vectorization;

namespace Rinha2026.Tests;

public sealed class FraudVectorizerTests {
	private static readonly MccRiskTable DefaultRiskTable = MccRiskTable.CreateDefault();

	[Fact]
	public void WriteVectorMatchesOfficialLegitExample() {
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

		Assert.True(ReferenceFraudRequestParser.TryParse(payload, out var request));

		Span<float> vector = stackalloc float[FraudVectorizer.PaddedDimension];
		FraudVectorizer.WriteVector(request, NormalizationConstants.Default, DefaultRiskTable, vector);

		AssertVectorClose(
			vector,
			[
				0.0041f, 0.1667f, 0.05f, 0.7826f, 0.3333f, -1f, -1f,
				0.0292f, 0.15f, 0f, 1f, 0f, 0.15f, 0.006f,
			]);
	}

	[Fact]
	public void WriteVectorMatchesOfficialFraudExample() {
		byte[] payload = Encoding.UTF8.GetBytes("""
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

		Assert.True(ReferenceFraudRequestParser.TryParse(payload, out var request));

		Span<float> vector = stackalloc float[FraudVectorizer.PaddedDimension];
		FraudVectorizer.WriteVector(request, NormalizationConstants.Default, DefaultRiskTable, vector);

		AssertVectorClose(
			vector,
			[
				0.9506f, 0.8333f, 1.0f, 0.2174f, 0.8333f, -1f, -1f,
				0.9523f, 1.0f, 0f, 1f, 1f, 0.75f, 0.0055f,
			]);
	}

	private static void AssertVectorClose(ReadOnlySpan<float> actual, ReadOnlySpan<float> expected) {
		for (int index = 0; index < expected.Length; index++) {
			Assert.InRange(actual[index], expected[index] - 0.0006f, expected[index] + 0.0006f);
		}

		Assert.Equal(0f, actual[14]);
		Assert.Equal(0f, actual[15]);
	}
}
