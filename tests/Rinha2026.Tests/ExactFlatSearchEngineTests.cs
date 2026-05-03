using Rinha2026.Core.Indexing;
using Rinha2026.Core.Search;
using Rinha2026.Tests.Support;

namespace Rinha2026.Tests;

public sealed class ExactFlatSearchEngineTests {
	[Fact]
	public async Task SearchReturnsNearestNeighborsInAscendingDistanceOrder() {
		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			(CreateVector(0.10f, 0.10f, 0.10f), "legit"),
			(CreateVector(0.20f, 0.20f, 0.20f), "fraud"),
			(CreateVector(0.90f, 0.90f, 0.90f), "fraud"),
			(CreateVector(0.15f, 0.15f, 0.15f), "legit"));

		using FlatArtifactSet artifactSet = FlatArtifactSet.Load(corpus.IndexDirectory);
		ExactFlatSearchEngine engine = new(artifactSet);
		SearchHit[] hits = new SearchHit[3];

		int count = engine.Search(CreateQuery(0.16f, 0.16f, 0.16f), hits);

		Assert.Equal(3, count);
		Assert.True(hits[0].Distance <= hits[1].Distance);
		Assert.True(hits[1].Distance <= hits[2].Distance);
		Assert.False(hits[0].IsFraud);
		Assert.True(hits[1].IsFraud);
		Assert.False(hits[2].IsFraud);
		Assert.InRange(hits[0].Distance, 0.0002f, 0.0005f);
		Assert.InRange(hits[1].Distance, 0.004f, 0.006f);
		Assert.InRange(hits[2].Distance, 0.009f, 0.012f);
	}

	[Fact]
	public async Task CountFraudCountsOnlyFraudNeighborsInsideTopK() {
		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			(CreateVector(0.10f, 0.10f, 0.10f), "legit"),
			(CreateVector(0.20f, 0.20f, 0.20f), "fraud"),
			(CreateVector(0.30f, 0.30f, 0.30f), "fraud"),
			(CreateVector(0.40f, 0.40f, 0.40f), "legit"));

		using FlatArtifactSet artifactSet = FlatArtifactSet.Load(corpus.IndexDirectory);
		ExactFlatSearchEngine engine = new(artifactSet);
		SearchHit[] hits = new SearchHit[3];

		int fraudCount = engine.CountFraud(CreateQuery(0.24f, 0.24f, 0.24f), hits);

		Assert.Equal(2, fraudCount);
		Assert.True(hits[0].Distance <= hits[1].Distance);
		Assert.True(hits[1].Distance <= hits[2].Distance);
		Assert.True(hits[0].IsFraud);
		Assert.True(hits[1].IsFraud);
		Assert.False(hits[2].IsFraud);
	}

	private static float[] CreateQuery(float first, float second, float third) {
		float[] query = new float[16];
		query[0] = first;
		query[1] = second;
		query[2] = third;
		return query;
	}

	private static float[] CreateVector(float first, float second, float third) {
		float[] vector = new float[14];
		vector[0] = first;
		vector[1] = second;
		vector[2] = third;
		return vector;
	}
}
