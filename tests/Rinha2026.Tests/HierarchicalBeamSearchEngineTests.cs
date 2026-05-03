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

	[Fact]
	public async Task LeafRadiusPruningSkipsDistantLeavesWithoutChangingTopHits() {
		IndexBuildOptions buildOptions = new() {
			InputPath = string.Empty,
			KMeansIterations = 4,
			Level1ClusterCount = 1,
			Level2ClustersPerLevel1 = 2,
			OutputDirectory = string.Empty,
			TrainingSampleSize = 8,
		};

		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			buildOptions,
			(CreateVector(0.10f, 0.10f, 0.10f), "legit"),
			(CreateVector(0.11f, 0.11f, 0.11f), "fraud"),
			(CreateVector(0.12f, 0.12f, 0.12f), "legit"),
			(CreateVector(0.13f, 0.13f, 0.13f), "fraud"),
			(CreateVector(0.84f, 0.84f, 0.84f), "legit"),
			(CreateVector(0.86f, 0.86f, 0.86f), "fraud"),
			(CreateVector(0.88f, 0.88f, 0.88f), "legit"),
			(CreateVector(0.90f, 0.90f, 0.90f), "fraud"));

		using HierarchicalArtifactSet hierarchicalArtifacts = HierarchicalArtifactSet.Load(corpus.IndexDirectory);
		HierarchicalBeamSearchEngine baselineEngine = new(
			hierarchicalArtifacts,
			useLastTransactionPartitionPruning: false,
			useLeafRadiusPruning: false);
		HierarchicalBeamSearchEngine prunedEngine = new(
			hierarchicalArtifacts,
			useLastTransactionPartitionPruning: false,
			useLeafRadiusPruning: true);
		float[] query = CreateQuery(0.115f, 0.115f, 0.115f);
		SearchHit[] baselineHits = new SearchHit[2];
		SearchHit[] prunedHits = new SearchHit[2];

		int baselineCount = baselineEngine.Search(query, beamLevel1: 1, beamLevel2: 2, rerankCount: 2, baselineHits);
		int prunedCount = prunedEngine.Search(query, beamLevel1: 1, beamLevel2: 2, rerankCount: 2, prunedHits);
		HierarchicalSearchTrace baselineTrace = baselineEngine.Trace(query, beamLevel1: 1, beamLevel2: 2, rerankCount: 2, topK: 2);
		HierarchicalSearchTrace prunedTrace = prunedEngine.Trace(query, beamLevel1: 1, beamLevel2: 2, rerankCount: 2, topK: 2);

		Assert.Equal(baselineCount, prunedCount);
		Assert.Equal(
			baselineHits.Take(baselineCount).Select(static hit => hit.Index),
			prunedHits.Take(prunedCount).Select(static hit => hit.Index));
		Assert.True(prunedTrace.PrunedLeafCount > 0);
		Assert.True(prunedTrace.CandidateScanCount < baselineTrace.CandidateScanCount);
	}

	[Fact]
	public async Task HistoryPartitionPruningSkipsOppositePartitionWhenSamePartitionAlreadyFillsTopK() {
		IndexBuildOptions buildOptions = new() {
			InputPath = string.Empty,
			KMeansIterations = 2,
			Level1ClusterCount = 1,
			Level2ClustersPerLevel1 = 1,
			OutputDirectory = string.Empty,
			TrainingSampleSize = 8,
			UseLastTransactionPartitioning = true,
		};

		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			buildOptions,
			(CreateVector(0.10f, 0.10f, 0.10f, hasLastTransaction: false), "legit"),
			(CreateVector(0.12f, 0.12f, 0.12f, hasLastTransaction: false), "legit"),
			(CreateVector(0.14f, 0.14f, 0.14f, hasLastTransaction: false), "legit"),
			(CreateVector(0.16f, 0.16f, 0.16f, hasLastTransaction: false), "fraud"),
			(CreateVector(0.18f, 0.18f, 0.18f, hasLastTransaction: false), "fraud"),
			(CreateVector(0.10f, 0.10f, 0.10f, hasLastTransaction: true), "legit"),
			(CreateVector(0.12f, 0.12f, 0.12f, hasLastTransaction: true), "fraud"),
			(CreateVector(0.14f, 0.14f, 0.14f, hasLastTransaction: true), "legit"));

		using HierarchicalArtifactSet hierarchicalArtifacts = HierarchicalArtifactSet.Load(corpus.IndexDirectory);
		HierarchicalBeamSearchEngine hierarchicalEngine = new(hierarchicalArtifacts);
		HierarchicalSearchTrace trace = hierarchicalEngine.Trace(
			CreateQuery(0.15f, 0.15f, 0.15f, hasLastTransaction: false),
			beamLevel1: 1,
			beamLevel2: 1,
			rerankCount: 8,
			topK: 5);

		Assert.Equal(5, trace.CandidateScanCount);
		Assert.Equal(0, trace.SecondaryCandidateScanCount);
	}

	[Fact]
	public async Task HistoryPartitionPruningFallsBackWhenSamePartitionCannotFillTopK() {
		IndexBuildOptions buildOptions = new() {
			InputPath = string.Empty,
			KMeansIterations = 2,
			Level1ClusterCount = 1,
			Level2ClustersPerLevel1 = 1,
			OutputDirectory = string.Empty,
			TrainingSampleSize = 8,
			UseLastTransactionPartitioning = true,
		};

		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			buildOptions,
			(CreateVector(0.10f, 0.10f, 0.10f, hasLastTransaction: false), "legit"),
			(CreateVector(0.12f, 0.12f, 0.12f, hasLastTransaction: false), "legit"),
			(CreateVector(0.14f, 0.14f, 0.14f, hasLastTransaction: false), "legit"),
			(CreateVector(0.16f, 0.16f, 0.16f, hasLastTransaction: false), "fraud"),
			(CreateVector(0.10f, 0.10f, 0.10f, hasLastTransaction: true), "legit"),
			(CreateVector(0.12f, 0.12f, 0.12f, hasLastTransaction: true), "fraud"),
			(CreateVector(0.14f, 0.14f, 0.14f, hasLastTransaction: true), "legit"),
			(CreateVector(0.16f, 0.16f, 0.16f, hasLastTransaction: true), "fraud"));

		using HierarchicalArtifactSet hierarchicalArtifacts = HierarchicalArtifactSet.Load(corpus.IndexDirectory);
		HierarchicalBeamSearchEngine hierarchicalEngine = new(hierarchicalArtifacts);
		HierarchicalSearchTrace trace = hierarchicalEngine.Trace(
			CreateQuery(0.15f, 0.15f, 0.15f, hasLastTransaction: false),
			beamLevel1: 1,
			beamLevel2: 1,
			rerankCount: 8,
			topK: 5);

		Assert.Equal(8, trace.CandidateScanCount);
		Assert.Equal(4, trace.SecondaryCandidateScanCount);
	}

	private static float[] CreateQuery(float first, float second, float third, bool hasLastTransaction = false) {
		float[] query = new float[16];
		query[0] = first;
		query[1] = second;
		query[2] = third;
		query[5] = hasLastTransaction ? 0.20f : -1f;
		query[6] = hasLastTransaction ? 0.30f : -1f;
		return query;
	}

	private static float[] CreateVector(float first, float second, float third, bool hasLastTransaction = false) {
		float[] vector = new float[14];
		vector[0] = first;
		vector[1] = second;
		vector[2] = third;
		vector[5] = hasLastTransaction ? 0.20f : -1f;
		vector[6] = hasLastTransaction ? 0.30f : -1f;
		return vector;
	}
}
