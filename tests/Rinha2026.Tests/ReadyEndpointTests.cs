using System.Net;

using Rinha2026.Tests.Support;

namespace Rinha2026.Tests;

public sealed class ReadyEndpointTests {
	[Fact]
	public async Task ReadyReturnsOkAfterStartup() {
		using TemporaryRuntimeData runtimeData = await TemporaryRuntimeData.CreateAsync(
			(CreateVector(0.1f, 0.1f, 0.1f), "legit"));
		using var factory = TestApiFactory.Create(runtimeData);
		using HttpClient client = factory.CreateClient();

		HttpResponseMessage response = await client.GetAsync("/ready");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	private static float[] CreateVector(float first, float second, float third) {
		float[] vector = new float[14];
		vector[0] = first;
		vector[1] = second;
		vector[2] = third;
		return vector;
	}
}
