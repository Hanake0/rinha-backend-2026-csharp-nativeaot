using System.Runtime.InteropServices;

using Rinha2026.Core.Indexing;
using Rinha2026.Core.Search;

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
		int[] leafWithoutHistoryCounts = new int[leafCounts.Length];
		int[] orderedOriginalIds = options.UseLastTransactionPartitioning
			? BuildPostingIdsWithHistoryPartition(
				assignments,
				postingOffsets,
				leafWithoutHistoryCounts,
				quantizedVectors.GetSpan(),
				paddedDimension)
			: BuildPostingIds(assignments, postingOffsets);
		float[] leafRadiusBounds = BuildLeafRadiusBounds(
			assignments,
			quantizedVectors.GetSpan(),
			paddedDimension,
			leafCentroids);
		RewriteFlatArtifactsInLeafOrder(options.OutputDirectory, baseManifest, quantizedVectors, orderedOriginalIds);
		int[] postingIds = BuildIdentityPostingIds(vectorCount);

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
		await WriteFloatArrayAsync(
			Path.Combine(options.OutputDirectory, "leaf.radius.q8.f32.bin"),
			leafRadiusBounds,
			cancellationToken);

		if (options.UseLastTransactionPartitioning) {
			await WriteIntArrayAsync(
				Path.Combine(options.OutputDirectory, "leaf.without-history.counts.bin"),
				leafWithoutHistoryCounts,
				cancellationToken);
		}

		return new IndexManifest {
			Dimension = baseManifest.Dimension,
			IndexKind = "HierarchicalBeamIvf",
			KMeansIterations = options.KMeansIterations,
			LabelBitsetFile = baseManifest.LabelBitsetFile,
			LabelEncoding = baseManifest.LabelEncoding,
			LeafCentroidFile = baseManifest.LeafCentroidFile,
			LeafRadiusFile = "leaf.radius.q8.f32.bin",
			LeafPostingIdsFile = baseManifest.LeafPostingIdsFile,
			LeafPostingOffsetsFile = baseManifest.LeafPostingOffsetsFile,
			LeafWithoutHistoryCountFile = options.UseLastTransactionPartitioning
				? "leaf.without-history.counts.bin"
				: string.Empty,
			Level1CentroidFile = baseManifest.Level1CentroidFile,
			Level1ClusterCount = level1ClusterCount,
			Level2ClustersPerLevel1 = level2ClustersPerLevel1,
			OriginalVectorIdFile = baseManifest.OriginalVectorIdFile,
			PaddedDimension = baseManifest.PaddedDimension,
			PostingLayout = "IdentityLeafOrder",
			QuantizationMaxValue = baseManifest.QuantizationMaxValue,
			QuantizationMinValue = baseManifest.QuantizationMinValue,
			QuantizationKind = baseManifest.QuantizationKind,
			QuantizationScale = baseManifest.QuantizationScale,
			QuantizedVectorFile = baseManifest.QuantizedVectorFile,
			FullPrecisionRerankVectorFile = baseManifest.FullPrecisionRerankVectorFile,
			RerankVectorFile = baseManifest.RerankVectorFile,
			TrainingSampleSize = trainingSampleSize,
			VectorCount = baseManifest.VectorCount,
		};
	}

	private static int[] BuildIdentityPostingIds(int vectorCount) {
		int[] postingIds = new int[vectorCount];

		for (int index = 0; index < postingIds.Length; index++) {
			postingIds[index] = index;
		}

		return postingIds;
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

	private static int[] BuildPostingIdsWithHistoryPartition(
		ReadOnlySpan<int> assignments,
		ReadOnlySpan<int> postingOffsets,
		Span<int> leafWithoutHistoryCounts,
		ReadOnlySpan<byte> quantizedVectors,
		int paddedDimension) {
		for (int vectorIndex = 0; vectorIndex < assignments.Length; vectorIndex++) {
			if (IsWithoutHistoryVector(quantizedVectors, vectorIndex, paddedDimension)) {
				leafWithoutHistoryCounts[assignments[vectorIndex]]++;
			}
		}

		int[] postingIds = new int[assignments.Length];
		int[] withoutHistoryCursors = postingOffsets.ToArray();
		int[] withHistoryCursors = new int[postingOffsets.Length - 1];

		for (int leafIndex = 0; leafIndex < withHistoryCursors.Length; leafIndex++) {
			withHistoryCursors[leafIndex] = postingOffsets[leafIndex] + leafWithoutHistoryCounts[leafIndex];
		}

		for (int vectorIndex = 0; vectorIndex < assignments.Length; vectorIndex++) {
			int leafId = assignments[vectorIndex];

			if (IsWithoutHistoryVector(quantizedVectors, vectorIndex, paddedDimension)) {
				postingIds[withoutHistoryCursors[leafId]++] = vectorIndex;
			} else {
				postingIds[withHistoryCursors[leafId]++] = vectorIndex;
			}
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

	private static float[] BuildLeafRadiusBounds(
		ReadOnlySpan<int> assignments,
		ReadOnlySpan<byte> quantizedVectors,
		int paddedDimension,
		ReadOnlySpan<float> leafCentroids) {
		int leafCount = leafCentroids.Length / paddedDimension;
		int[] maxSquaredDistances = new int[leafCount];
		sbyte[] quantizedLeafCentroids = new sbyte[leafCentroids.Length];
		VectorEncoding.EncodeQ8Symmetric(leafCentroids, quantizedLeafCentroids);

		for (int vectorIndex = 0; vectorIndex < assignments.Length; vectorIndex++) {
			int leafId = assignments[vectorIndex];
			int vectorOffset = checked(vectorIndex * paddedDimension);
			int centroidOffset = checked(leafId * paddedDimension);
			int squaredDistance = ComputeSquaredL2Q8(
				MemoryMarshal.Cast<byte, sbyte>(quantizedVectors.Slice(vectorOffset, paddedDimension)),
				quantizedLeafCentroids.AsSpan(centroidOffset, paddedDimension));

			if (squaredDistance > maxSquaredDistances[leafId]) {
				maxSquaredDistances[leafId] = squaredDistance;
			}
		}

		float[] radiusBounds = new float[maxSquaredDistances.Length];

		for (int leafIndex = 0; leafIndex < radiusBounds.Length; leafIndex++) {
			radiusBounds[leafIndex] = MathF.Sqrt(maxSquaredDistances[leafIndex]);
		}

		return radiusBounds;
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

	private static void RewriteFlatArtifactsInLeafOrder(
		string outputDirectory,
		IndexManifest manifest,
		MemoryMappedReadOnlyBuffer quantizedVectors,
		ReadOnlySpan<int> orderedOriginalIds) {
		string q8Path = Path.Combine(outputDirectory, manifest.QuantizedVectorFile);
		string f16Path = Path.Combine(outputDirectory, manifest.RerankVectorFile);
		string f32Path = string.IsNullOrWhiteSpace(manifest.FullPrecisionRerankVectorFile)
			? string.Empty
			: Path.Combine(outputDirectory, manifest.FullPrecisionRerankVectorFile);
		string originalIdsPath = string.IsNullOrWhiteSpace(manifest.OriginalVectorIdFile)
			? string.Empty
			: Path.Combine(outputDirectory, manifest.OriginalVectorIdFile);
		string labelPath = Path.Combine(outputDirectory, manifest.LabelBitsetFile);
		string q8TempPath = q8Path + ".reordered";
		string f16TempPath = f16Path + ".reordered";
		string f32TempPath = string.IsNullOrWhiteSpace(f32Path) ? string.Empty : f32Path + ".reordered";
		string originalIdsTempPath = string.IsNullOrWhiteSpace(originalIdsPath) ? string.Empty : originalIdsPath + ".reordered";
		string labelTempPath = labelPath + ".reordered";
		int q8Width = manifest.PaddedDimension;
		int f16Width = checked(manifest.PaddedDimension * sizeof(ushort));
		int f32Width = checked(manifest.PaddedDimension * sizeof(float));

		using (MemoryMappedReadOnlyBuffer rerankVectors = MemoryMappedReadOnlyBuffer.OpenRead(f16Path))
		using (MemoryMappedReadOnlyBuffer? fullPrecisionRerankVectors = string.IsNullOrWhiteSpace(f32Path)
			? null
			: MemoryMappedReadOnlyBuffer.OpenRead(f32Path))
		using (MemoryMappedReadOnlyBuffer? originalVectorIds = string.IsNullOrWhiteSpace(originalIdsPath)
			? null
			: MemoryMappedReadOnlyBuffer.OpenRead(originalIdsPath))
		using (MemoryMappedReadOnlyBuffer labels = MemoryMappedReadOnlyBuffer.OpenRead(labelPath))
		using (FileStream q8Stream = CreateOutputStream(q8TempPath))
		using (FileStream f16Stream = CreateOutputStream(f16TempPath))
		using (FileStream? f32Stream = string.IsNullOrWhiteSpace(f32TempPath)
			? null
			: CreateOutputStream(f32TempPath))
		using (FileStream? originalIdsStream = string.IsNullOrWhiteSpace(originalIdsTempPath)
			? null
			: CreateOutputStream(originalIdsTempPath)) {
			ReadOnlySpan<byte> quantizedSpan = quantizedVectors.GetSpan();
			ReadOnlySpan<byte> rerankSpan = rerankVectors.GetSpan();
			ReadOnlySpan<byte> fullPrecisionRerankSpan = (fullPrecisionRerankVectors is null)
				? ReadOnlySpan<byte>.Empty
				: fullPrecisionRerankVectors.GetSpan();
			ReadOnlySpan<int> originalIdSpan = (originalVectorIds is null)
				? ReadOnlySpan<int>.Empty
				: MemoryMarshal.Cast<byte, int>(originalVectorIds.GetSpan());
			ReadOnlySpan<byte> labelSpan = labels.GetSpan();
			byte[] reorderedLabels = new byte[(orderedOriginalIds.Length + 7) / 8];
			byte[] originalIdBuffer = new byte[sizeof(int)];

			for (int destinationIndex = 0; destinationIndex < orderedOriginalIds.Length; destinationIndex++) {
				int sourceIndex = orderedOriginalIds[destinationIndex];
				q8Stream.Write(quantizedSpan.Slice(sourceIndex * q8Width, q8Width));
				f16Stream.Write(rerankSpan.Slice(sourceIndex * f16Width, f16Width));

				if (f32Stream is not null) {
					f32Stream.Write(fullPrecisionRerankSpan.Slice(sourceIndex * f32Width, f32Width));
				}

				if (originalIdsStream is not null) {
					BitConverter.TryWriteBytes(originalIdBuffer, originalIdSpan[sourceIndex]);
					originalIdsStream.Write(originalIdBuffer);
				}

				if (IsFraud(labelSpan, sourceIndex)) {
					reorderedLabels[destinationIndex >> 3] |= (byte)(1 << (destinationIndex & 0b111));
				}
			}

			File.WriteAllBytes(labelTempPath, reorderedLabels);
		}

		quantizedVectors.Dispose();
		File.Delete(q8Path);
		File.Delete(f16Path);
		if (!string.IsNullOrWhiteSpace(f32Path)) {
			File.Delete(f32Path);
		}
		if (!string.IsNullOrWhiteSpace(originalIdsPath)) {
			File.Delete(originalIdsPath);
		}
		File.Delete(labelPath);
		File.Move(q8TempPath, q8Path);
		File.Move(f16TempPath, f16Path);
		if (!string.IsNullOrWhiteSpace(f32TempPath) && !string.IsNullOrWhiteSpace(f32Path)) {
			File.Move(f32TempPath, f32Path);
		}
		if (!string.IsNullOrWhiteSpace(originalIdsTempPath) && !string.IsNullOrWhiteSpace(originalIdsPath)) {
			File.Move(originalIdsTempPath, originalIdsPath);
		}
		File.Move(labelTempPath, labelPath);
	}

	private static FileStream CreateOutputStream(string path) => new(
		path,
		FileMode.Create,
		FileAccess.Write,
		FileShare.None,
		bufferSize: 64 * 1024,
		FileOptions.SequentialScan);

	private static bool IsFraud(ReadOnlySpan<byte> labels, int vectorIndex) {
		int byteOffset = vectorIndex >> 3;
		int bitOffset = vectorIndex & 0b111;
		return (labels[byteOffset] & (1 << bitOffset)) != 0;
	}

	private static bool IsWithoutHistoryVector(
		ReadOnlySpan<byte> quantizedVectors,
		int vectorIndex,
		int paddedDimension) {
		int vectorOffset = checked(vectorIndex * paddedDimension);
		ReadOnlySpan<sbyte> vector = MemoryMarshal.Cast<byte, sbyte>(
			quantizedVectors.Slice(vectorOffset, paddedDimension));
		return (vector[5] == -127) && (vector[6] == -127);
	}

	private static int ComputeSquaredL2Q8(ReadOnlySpan<sbyte> left, ReadOnlySpan<sbyte> right) {
		return DistanceComputations.SquaredL2Q8(left, right);
	}
}
