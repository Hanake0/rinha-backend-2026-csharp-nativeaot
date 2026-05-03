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
		Assert.Equal(3, hits[0].Index);
		Assert.Equal(1, hits[1].Index);
		Assert.Equal(0, hits[2].Index);
		Assert.True(hits[0].Distance <= hits[1].Distance);
		Assert.True(hits[1].Distance <= hits[2].Distance);
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
		Assert.Equal([1, 2, 0], hits.Select(static hit => hit.Index).ToArray());
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
