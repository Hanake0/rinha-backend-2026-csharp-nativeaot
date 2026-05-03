using Rinha2026.Core.Indexing;
using Rinha2026.IndexBuilder.Build;
using Rinha2026.Tests.Support;

namespace Rinha2026.Tests;

public sealed class HierarchicalArtifactSetTests {
	[Fact]
	public async Task LoadReadsCentroidsAndPostingLists() {
		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			(CreateVector(0.05f, 0.05f, 0.05f), "legit"),
			(CreateVector(0.10f, 0.10f, 0.10f), "fraud"),
			(CreateVector(0.90f, 0.90f, 0.90f), "legit"),
			(CreateVector(0.95f, 0.95f, 0.95f), "fraud"));

		using HierarchicalArtifactSet artifactSet = HierarchicalArtifactSet.Load(corpus.IndexDirectory);

		Assert.True(artifactSet.Level1ClusterCount > 0);
		Assert.True(artifactSet.Level2ClustersPerLevel1 > 0);
		Assert.Equal(artifactSet.LeafCount + 1, artifactSet.GetLeafPostingOffsets().Length);
		Assert.Equal(4, artifactSet.GetLeafPostingIds().Length);
		Assert.Equal(artifactSet.Level1ClusterCount * 16, artifactSet.GetLevel1Centroids().Length);
		Assert.Equal(artifactSet.LeafCount * 16, artifactSet.GetLeafCentroids().Length);
		Assert.False(artifactSet.HasLastTransactionPartitioning);
	}

	[Fact]
	public async Task LoadReadsLastTransactionPartitionCountsWhenEnabled() {
		IndexBuildOptions buildOptions = new() {
			InputPath = string.Empty,
			KMeansIterations = 2,
			Level1ClusterCount = 2,
			Level2ClustersPerLevel1 = 2,
			OutputDirectory = string.Empty,
			TrainingSampleSize = 4,
			UseLastTransactionPartitioning = true,
		};

		using TemporaryArtifactCorpus corpus = await TemporaryArtifactCorpus.CreateAsync(
			buildOptions,
			(CreateVector(0.05f, 0.05f, 0.05f, hasLastTransaction: false), "legit"),
			(CreateVector(0.10f, 0.10f, 0.10f, hasLastTransaction: false), "fraud"),
			(CreateVector(0.90f, 0.90f, 0.90f, hasLastTransaction: true), "legit"),
			(CreateVector(0.95f, 0.95f, 0.95f, hasLastTransaction: true), "fraud"));

		using HierarchicalArtifactSet artifactSet = HierarchicalArtifactSet.Load(corpus.IndexDirectory);

		Assert.True(artifactSet.HasLastTransactionPartitioning);
		Assert.Equal(2, artifactSet.GetLeafWithoutHistoryCounts().ToArray().Sum());
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
