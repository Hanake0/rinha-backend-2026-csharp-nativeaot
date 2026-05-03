using System.Net;
using System.Text;

using Rinha2026.Core.Configuration;
using Rinha2026.Tests.Support;

namespace Rinha2026.Tests;

public sealed class FraudScoreEndpointTests {
	[Fact]
	public async Task FraudScoreReturnsExpectedDecisionAndScore() {
		float[] queryVector = [
			0.0041f, 0.1667f, 0.05f, 0.7826f, 0.3333f, -1f, -1f,
			0.0292f, 0.15f, 0f, 1f, 0f, 0.15f, 0.006f,
		];

		using TemporaryRuntimeData runtimeData = await TemporaryRuntimeData.CreateAsync(
			(queryVector.ToArray(), "fraud"),
			(queryVector.ToArray(), "fraud"),
			(queryVector.ToArray(), "fraud"),
			(queryVector.ToArray(), "fraud"),
			(queryVector.ToArray(), "legit"));
		using var factory = TestApiFactory.Create(runtimeData, IndexKind.ExactSampleOnly);
		using HttpClient client = factory.CreateClient();
		using StringContent content = new(
			"""
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
			""",
			Encoding.UTF8,
			"application/json");

		HttpResponseMessage response = await client.PostAsync("/fraud-score", content);
		string payload = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("""{"approved":false,"fraud_score":0.8}""", payload);
	}
}
