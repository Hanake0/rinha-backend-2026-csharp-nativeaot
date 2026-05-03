using Rinha2026.Core.Indexing;
using Rinha2026.Core.Search;
using Rinha2026.IndexBuilder.Build;
using Rinha2026.Tests.Support;

namespace Rinha2026.Tests;

public sealed class HierarchicalBeamSearchEngineTests {
	[Fact]
	public async Task FullBeamSearchMatchesExactBaseline() {
		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			(CreateVector(0.05f, 0.05f, 0.05f), "legit"),
			(CreateVector(0.10f, 0.10f, 0.10f), "fraud"),
			(CreateVector(0.15f, 0.15f, 0.15f), "legit"),
			(CreateVector(0.20f, 0.20f, 0.20f), "fraud"),
			(CreateVector(0.80f, 0.80f, 0.80f), "legit"),
			(CreateVector(0.85f, 0.85f, 0.85f), "fraud"),
			(CreateVector(0.90f, 0.90f, 0.90f), "legit"),
			(CreateVector(0.95f, 0.95f, 0.95f), "fraud"));

		using FlatArtifactSet flatArtifacts = FlatArtifactSet.Load(corpus.IndexDirectory);
		using HierarchicalArtifactSet hierarchicalArtifacts = HierarchicalArtifactSet.Load(corpus.IndexDirectory);
		ExactFlatSearchEngine exactEngine = new(flatArtifacts);
		HierarchicalBeamSearchEngine hierarchicalEngine = new(hierarchicalArtifacts);
		float[] query = CreateQuery(0.18f, 0.18f, 0.18f);
		SearchHit[] exactHits = new SearchHit[4];
		SearchHit[] hierarchicalHits = new SearchHit[4];

		int exactCount = exactEngine.Search(query, exactHits);
		int hierarchicalCount = hierarchicalEngine.Search(
			query,
			hierarchicalArtifacts.Level1ClusterCount,
			hierarchicalArtifacts.LeafCount,
			(int)flatArtifacts.VectorCount,
			hierarchicalHits);

		Assert.Equal(exactCount, hierarchicalCount);
		Assert.Equal(
			exactHits.Take(exactCount).Select(static hit => hit.Index),
			hierarchicalHits.Take(hierarchicalCount).Select(static hit => hit.Index));
	}

	[Fact]
	public async Task ReducedBeamSearchKeepsClusterLocalNeighbors() {
		IndexBuildOptions buildOptions = new() {
			InputPath = string.Empty,
			KMeansIterations = 4,
			Level1ClusterCount = 2,
			Level2ClustersPerLevel1 = 2,
			OutputDirectory = string.Empty,
			TrainingSampleSize = 8,
		};

		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			buildOptions,
			(CreateVector(0.02f, 0.02f, 0.02f), "legit"),
			(CreateVector(0.04f, 0.04f, 0.04f), "fraud"),
			(CreateVector(0.06f, 0.06f, 0.06f), "legit"),
			(CreateVector(0.08f, 0.08f, 0.08f), "fraud"),
			(CreateVector(0.92f, 0.92f, 0.92f), "legit"),
			(CreateVector(0.94f, 0.94f, 0.94f), "fraud"),
			(CreateVector(0.96f, 0.96f, 0.96f), "legit"),
			(CreateVector(0.98f, 0.98f, 0.98f), "fraud"));

		using HierarchicalArtifactSet hierarchicalArtifacts = HierarchicalArtifactSet.Load(corpus.IndexDirectory);
		HierarchicalBeamSearchEngine hierarchicalEngine = new(hierarchicalArtifacts);
		SearchHit[] hits = new SearchHit[3];

		int count = hierarchicalEngine.Search(
			CreateQuery(0.05f, 0.05f, 0.05f),
			beamLevel1: 1,
			beamLevel2: Math.Min(2, hierarchicalArtifacts.LeafCount),
			rerankCount: 8,
			hits);

		Assert.Equal(3, count);
		Assert.All(hits, static hit => Assert.InRange(hit.Distance, 0f, 0.01f));
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
