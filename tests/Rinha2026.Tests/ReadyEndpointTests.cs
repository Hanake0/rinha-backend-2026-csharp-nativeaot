using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Rinha2026.Tests;

public sealed class ReadyEndpointTests : IClassFixture<WebApplicationFactory<Program>> {
	private readonly WebApplicationFactory<Program> factory;

	public ReadyEndpointTests(WebApplicationFactory<Program> factory) {
		this.factory = factory;
	}

	[Fact]
	public async Task ReadyReturnsOkAfterStartup() {
		using HttpClient client = this.factory.CreateClient();

		HttpResponseMessage response = await client.GetAsync("/ready");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}
}
