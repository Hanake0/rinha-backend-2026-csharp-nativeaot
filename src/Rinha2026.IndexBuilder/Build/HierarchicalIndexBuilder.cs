using System.Runtime.InteropServices;

using Rinha2026.Core.Indexing;

namespace Rinha2026.IndexBuilder.Build;

internal static class HierarchicalIndexBuilder {
	public static async Task<IndexManifest> BuildAsync(
		IndexBuildOptions options,
		IndexManifest baseManifest,
		CancellationToken cancellationToken) {
		int vectorCount = checked((int)baseManifest.VectorCount);
		int paddedDimension = baseManifest.PaddedDimension;
		int level1ClusterCount = Math.Min(options.Level1ClusterCount, vectorCount);
		int level2ClustersPerLevel1 = options.Level2ClustersPerLevel1;
		int trainingSampleSize = Math.Min(options.TrainingSampleSize, vectorCount);
		string quantizedVectorPath = Path.Combine(options.OutputDirectory, baseManifest.QuantizedVectorFile);
		using MemoryMappedReadOnlyBuffer quantizedVectors = MemoryMappedReadOnlyBuffer.OpenRead(quantizedVectorPath);

		float[] sampleVectors = CollectTrainingSample(
			quantizedVectors.GetSpan(),
			vectorCount,
			paddedDimension,
			trainingSampleSize);
		float[] level1Centroids = KMeansTrainer.Train(
			sampleVectors,
			trainingSampleSize,
			paddedDimension,
			level1ClusterCount,
			options.KMeansIterations);
		float[] leafCentroids = TrainLeafCentroids(
			sampleVectors,
			trainingSampleSize,
			paddedDimension,
			level1Centroids,
			level1ClusterCount,
			level2ClustersPerLevel1,
			options.KMeansIterations);

		int[] assignments = new int[vectorCount];
		int[] leafCounts = new int[level1ClusterCount * level2ClustersPerLevel1];
		AssignAllVectorsToLeaves(
			quantizedVectors.GetSpan(),
			assignments,
			leafCounts,
			paddedDimension,
			level1Centroids,
			level1ClusterCount,
			leafCentroids,
			level2ClustersPerLevel1);

		int[] postingOffsets = BuildPostingOffsets(leafCounts);
		int[] postingIds = BuildPostingIds(assignments, postingOffsets);

		await WriteFloatArrayAsync(
			Path.Combine(options.OutputDirectory, baseManifest.Level1CentroidFile),
			level1Centroids,
			cancellationToken);
		await WriteFloatArrayAsync(
			Path.Combine(options.OutputDirectory, baseManifest.LeafCentroidFile),
			leafCentroids,
			cancellationToken);
		await WriteIntArrayAsync(
			Path.Combine(options.OutputDirectory, baseManifest.LeafPostingOffsetsFile),
			postingOffsets,
			cancellationToken);
		await WriteIntArrayAsync(
			Path.Combine(options.OutputDirectory, baseManifest.LeafPostingIdsFile),
			postingIds,
			cancellationToken);

		return new IndexManifest {
			Dimension = baseManifest.Dimension,
			IndexKind = "HierarchicalBeamIvf",
			KMeansIterations = options.KMeansIterations,
			LabelBitsetFile = baseManifest.LabelBitsetFile,
			LabelEncoding = baseManifest.LabelEncoding,
			LeafCentroidFile = baseManifest.LeafCentroidFile,
			LeafPostingIdsFile = baseManifest.LeafPostingIdsFile,
			LeafPostingOffsetsFile = baseManifest.LeafPostingOffsetsFile,
			Level1CentroidFile = baseManifest.Level1CentroidFile,
			Level1ClusterCount = level1ClusterCount,
			Level2ClustersPerLevel1 = level2ClustersPerLevel1,
			PaddedDimension = baseManifest.PaddedDimension,
			QuantizationMaxValue = baseManifest.QuantizationMaxValue,
			QuantizationMinValue = baseManifest.QuantizationMinValue,
			QuantizationKind = baseManifest.QuantizationKind,
			QuantizationScale = baseManifest.QuantizationScale,
			QuantizedVectorFile = baseManifest.QuantizedVectorFile,
			RerankVectorFile = baseManifest.RerankVectorFile,
			TrainingSampleSize = trainingSampleSize,
			VectorCount = baseManifest.VectorCount,
		};
	}

	private static int[] BuildPostingIds(ReadOnlySpan<int> assignments, ReadOnlySpan<int> postingOffsets) {
		int[] postingIds = new int[assignments.Length];
		int[] cursors = postingOffsets.ToArray();

		for (int vectorIndex = 0; vectorIndex < assignments.Length; vectorIndex++) {
			int leafId = assignments[vectorIndex];
			postingIds[cursors[leafId]++] = vectorIndex;
		}

		return postingIds;
	}

	private static int[] BuildPostingOffsets(ReadOnlySpan<int> leafCounts) {
		int[] postingOffsets = new int[leafCounts.Length + 1];
		int running = 0;

		for (int leafIndex = 0; leafIndex < leafCounts.Length; leafIndex++) {
			postingOffsets[leafIndex] = running;
			running += leafCounts[leafIndex];
		}

		postingOffsets[leafCounts.Length] = running;
		return postingOffsets;
	}

	private static void AssignAllVectorsToLeaves(
		ReadOnlySpan<byte> quantizedVectors,
		Span<int> assignments,
		Span<int> leafCounts,
		int paddedDimension,
		ReadOnlySpan<float> level1Centroids,
		int level1ClusterCount,
		ReadOnlySpan<float> leafCentroids,
		int level2ClustersPerLevel1) {
		float[] vectorBuffer = new float[paddedDimension];

		for (int vectorIndex = 0; vectorIndex < assignments.Length; vectorIndex++) {
			VectorEncoding.DecodeQ8Symmetric(
				quantizedVectors.Slice(vectorIndex * paddedDimension, paddedDimension),
				vectorBuffer);
			int parentId = KMeansTrainer.FindNearestCentroid(vectorBuffer, level1Centroids, level1ClusterCount, paddedDimension);
			int childBase = parentId * level2ClustersPerLevel1;
			int childId = KMeansTrainer.FindNearestCentroid(
				vectorBuffer,
				leafCentroids.Slice(childBase * paddedDimension, level2ClustersPerLevel1 * paddedDimension),
				level2ClustersPerLevel1,
				paddedDimension);
			int leafId = childBase + childId;
			assignments[vectorIndex] = leafId;
			leafCounts[leafId]++;
		}
	}

	private static float[] CollectTrainingSample(
		ReadOnlySpan<byte> quantizedVectors,
		int vectorCount,
		int paddedDimension,
		int sampleCount) {
		float[] samples = new float[sampleCount * paddedDimension];

		for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
			int vectorIndex = (int)(((long)sampleIndex * vectorCount) / sampleCount);
			VectorEncoding.DecodeQ8Symmetric(
				quantizedVectors.Slice(vectorIndex * paddedDimension, paddedDimension),
				samples.AsSpan(sampleIndex * paddedDimension, paddedDimension));
		}

		return samples;
	}

	private static float[] TrainLeafCentroids(
		ReadOnlySpan<float> sampleVectors,
		int sampleCount,
		int paddedDimension,
		ReadOnlySpan<float> level1Centroids,
		int level1ClusterCount,
		int level2ClustersPerLevel1,
		int kMeansIterations) {
		List<int>[] sampleIndexesByParent = new List<int>[level1ClusterCount];

		for (int parentIndex = 0; parentIndex < sampleIndexesByParent.Length; parentIndex++) {
			sampleIndexesByParent[parentIndex] = [];
		}

		for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++) {
			int parentId = KMeansTrainer.FindNearestCentroid(
				sampleVectors.Slice(sampleIndex * paddedDimension, paddedDimension),
				level1Centroids,
				level1ClusterCount,
				paddedDimension);
			sampleIndexesByParent[parentId].Add(sampleIndex);
		}

		float[] leafCentroids = new float[level1ClusterCount * level2ClustersPerLevel1 * paddedDimension];

		for (int parentIndex = 0; parentIndex < level1ClusterCount; parentIndex++) {
			Span<float> destination = leafCentroids.AsSpan(
				parentIndex * level2ClustersPerLevel1 * paddedDimension,
				level2ClustersPerLevel1 * paddedDimension);
			List<int> sampleIndexes = sampleIndexesByParent[parentIndex];

			if (sampleIndexes.Count == 0) {
				for (int childIndex = 0; childIndex < level2ClustersPerLevel1; childIndex++) {
					level1Centroids.Slice(parentIndex * paddedDimension, paddedDimension)
						.CopyTo(destination.Slice(childIndex * paddedDimension, paddedDimension));
				}

				continue;
			}

			float[] parentSamples = new float[sampleIndexes.Count * paddedDimension];

			for (int sampleSlot = 0; sampleSlot < sampleIndexes.Count; sampleSlot++) {
				sampleVectors.Slice(sampleIndexes[sampleSlot] * paddedDimension, paddedDimension)
					.CopyTo(parentSamples.AsSpan(sampleSlot * paddedDimension, paddedDimension));
			}

			float[] childCentroids = KMeansTrainer.Train(
				parentSamples,
				sampleIndexes.Count,
				paddedDimension,
				level2ClustersPerLevel1,
				kMeansIterations);
			childCentroids.CopyTo(destination);
		}

		return leafCentroids;
	}

	private static async Task WriteFloatArrayAsync(string path, float[] values, CancellationToken cancellationToken) {
		byte[] payload = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
		await File.WriteAllBytesAsync(path, payload, cancellationToken);
	}

	private static async Task WriteIntArrayAsync(string path, int[] values, CancellationToken cancellationToken) {
		byte[] payload = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
		await File.WriteAllBytesAsync(path, payload, cancellationToken);
	}
}
