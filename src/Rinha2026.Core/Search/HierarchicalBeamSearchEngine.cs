using Rinha2026.Core.Indexing;

namespace Rinha2026.Core.Search;

public sealed class HierarchicalBeamSearchEngine {
	private const float CrossHistoryLowerBoundSquaredL2 = 2f;

	private readonly HierarchicalArtifactSet artifactSet;
	private readonly bool useLastTransactionPartitionPruning;

	public HierarchicalBeamSearchEngine(
		HierarchicalArtifactSet artifactSet,
		bool useLastTransactionPartitionPruning = true) {
		this.artifactSet = artifactSet ?? throw new ArgumentNullException(nameof(artifactSet));
		this.useLastTransactionPartitionPruning = useLastTransactionPartitionPruning;
	}

	public int CountFraud(
		ReadOnlySpan<float> query,
		int beamLevel1,
		int beamLevel2,
		int rerankCount,
		Span<SearchHit> destination) {
		int matchCount = this.Search(query, beamLevel1, beamLevel2, rerankCount, destination);
		int fraudCount = 0;

		for (int index = 0; index < matchCount; index++) {
			if (destination[index].IsFraud) {
				fraudCount++;
			}
		}

		return fraudCount;
	}

	public int Search(
		ReadOnlySpan<float> query,
		int beamLevel1,
		int beamLevel2,
		int rerankCount,
		Span<SearchHit> destination) {
		if (destination.IsEmpty) {
			throw new ArgumentException("Destination span must not be empty.", nameof(destination));
		}

		FlatArtifactSet flatArtifacts = this.artifactSet.FlatArtifacts;

		if (query.Length < flatArtifacts.PaddedDimension) {
			throw new ArgumentException("Query vector does not match the padded index dimension.", nameof(query));
		}

		int parentBeamCount = Math.Clamp(beamLevel1, 1, this.artifactSet.Level1ClusterCount);
		int leafBeamCount = Math.Clamp(beamLevel2, 1, this.artifactSet.LeafCount);
		int candidateCapacity = Math.Clamp(
			rerankCount,
			destination.Length,
			checked((int)flatArtifacts.VectorCount));

		Span<int> parentIds = parentBeamCount <= 128 ? stackalloc int[parentBeamCount] : new int[parentBeamCount];
		Span<float> parentDistances = parentBeamCount <= 128 ? stackalloc float[parentBeamCount] : new float[parentBeamCount];
		Span<int> leafIds = leafBeamCount <= 512 ? stackalloc int[leafBeamCount] : new int[leafBeamCount];
		Span<float> leafDistances = leafBeamCount <= 512 ? stackalloc float[leafBeamCount] : new float[leafBeamCount];
		Span<int> candidateIds = candidateCapacity <= 512 ? stackalloc int[candidateCapacity] : new int[candidateCapacity];
		Span<int> candidateDistances = candidateCapacity <= 512 ? stackalloc int[candidateCapacity] : new int[candidateCapacity];
		Span<sbyte> quantizedQuery = stackalloc sbyte[flatArtifacts.PaddedDimension];
		int candidateMaxIndex = 0;
		int candidateMaxDistance = int.MinValue;

		VectorEncoding.EncodeQ8Symmetric(query[..flatArtifacts.PaddedDimension], quantizedQuery);

		int parentCount = this.SelectNearestParents(query, parentIds, parentDistances);
		int leafCount = this.SelectNearestLeaves(query, parentIds[..parentCount], leafIds, leafDistances);

		if (!this.ShouldUseLastTransactionPartitionPruning()) {
			int candidateCount = 0;
			candidateCount = this.SelectTopCandidates(
				quantizedQuery,
				leafIds[..leafCount],
				candidateIds,
				candidateDistances,
				ref candidateCount,
				ref candidateMaxIndex,
				ref candidateMaxDistance,
				CandidatePartitionSelection.All,
				queryWithoutHistory: false,
				out _,
				out _,
				out _);
			return this.RerankCandidates(query, candidateIds[..candidateCount], destination);
		}

		bool queryWithoutHistory = IsWithoutHistoryQuery(query);
		int candidateCountWithSamePartition = 0;
		candidateCountWithSamePartition = this.SelectTopCandidates(
			quantizedQuery,
			leafIds[..leafCount],
			candidateIds,
			candidateDistances,
			ref candidateCountWithSamePartition,
			ref candidateMaxIndex,
			ref candidateMaxDistance,
			CandidatePartitionSelection.SamePartitionOnly,
			queryWithoutHistory,
			out _,
			out _,
			out _);
		int matchCount = this.RerankCandidates(query, candidateIds[..candidateCountWithSamePartition], destination);

		if ((matchCount == destination.Length) &&
			(destination[matchCount - 1].Distance < CrossHistoryLowerBoundSquaredL2)) {
			return matchCount;
		}

		int mergedCandidateCount = candidateCountWithSamePartition;
		mergedCandidateCount = this.SelectTopCandidates(
			quantizedQuery,
			leafIds[..leafCount],
			candidateIds,
			candidateDistances,
			ref mergedCandidateCount,
			ref candidateMaxIndex,
			ref candidateMaxDistance,
			CandidatePartitionSelection.OppositePartitionOnly,
			queryWithoutHistory,
			out _,
			out _,
			out _);
		return this.RerankCandidates(query, candidateIds[..mergedCandidateCount], destination);
	}

	public HierarchicalSearchTrace Trace(
		ReadOnlySpan<float> query,
		int beamLevel1,
		int beamLevel2,
		int rerankCount,
		int topK) {
		FlatArtifactSet flatArtifacts = this.artifactSet.FlatArtifacts;

		if (query.Length < flatArtifacts.PaddedDimension) {
			throw new ArgumentException("Query vector does not match the padded index dimension.", nameof(query));
		}

		int parentBeamCount = Math.Clamp(beamLevel1, 1, this.artifactSet.Level1ClusterCount);
		int leafBeamCount = Math.Clamp(beamLevel2, 1, this.artifactSet.LeafCount);
		int candidateCapacity = Math.Clamp(
			rerankCount,
			1,
			checked((int)flatArtifacts.VectorCount));

		Span<int> parentIds = parentBeamCount <= 128 ? stackalloc int[parentBeamCount] : new int[parentBeamCount];
		Span<float> parentDistances = parentBeamCount <= 128 ? stackalloc float[parentBeamCount] : new float[parentBeamCount];
		Span<int> leafIds = leafBeamCount <= 512 ? stackalloc int[leafBeamCount] : new int[leafBeamCount];
		Span<float> leafDistances = leafBeamCount <= 512 ? stackalloc float[leafBeamCount] : new float[leafBeamCount];
		Span<int> candidateIds = candidateCapacity <= 512 ? stackalloc int[candidateCapacity] : new int[candidateCapacity];
		Span<int> candidateDistances = candidateCapacity <= 512 ? stackalloc int[candidateCapacity] : new int[candidateCapacity];
		Span<sbyte> quantizedQuery = stackalloc sbyte[flatArtifacts.PaddedDimension];
		Span<SearchHit> rerankHits = topK <= 16 ? stackalloc SearchHit[topK] : new SearchHit[topK];
		int candidateMaxIndex = 0;
		int candidateMaxDistance = int.MinValue;

		VectorEncoding.EncodeQ8Symmetric(query[..flatArtifacts.PaddedDimension], quantizedQuery);

		int parentCount = this.SelectNearestParents(query, parentIds, parentDistances);
		int leafCount = this.SelectNearestLeaves(query, parentIds[..parentCount], leafIds, leafDistances);

		if (!this.ShouldUseLastTransactionPartitionPruning()) {
			int candidateCount = 0;
			candidateCount = this.SelectTopCandidates(
				quantizedQuery,
				leafIds[..leafCount],
				candidateIds,
				candidateDistances,
				ref candidateCount,
				ref candidateMaxIndex,
				ref candidateMaxDistance,
				CandidatePartitionSelection.All,
				queryWithoutHistory: false,
				out int scannedCandidateCount,
				out int branchMaxSelectedLeafSize,
				out int branchMinSelectedLeafSize);
			return new HierarchicalSearchTrace(
				parentCount,
				leafCount,
				scannedCandidateCount,
				candidateCount,
				branchMaxSelectedLeafSize,
				branchMinSelectedLeafSize,
				SecondaryCandidateScanCount: 0);
		}

		bool queryWithoutHistory = IsWithoutHistoryQuery(query);
		int candidateCountWithSamePartition = 0;
		candidateCountWithSamePartition = this.SelectTopCandidates(
			quantizedQuery,
			leafIds[..leafCount],
			candidateIds,
			candidateDistances,
			ref candidateCountWithSamePartition,
			ref candidateMaxIndex,
			ref candidateMaxDistance,
			CandidatePartitionSelection.SamePartitionOnly,
			queryWithoutHistory,
			out int primaryScanCount,
			out int maxSelectedLeafSize,
			out int minSelectedLeafSize);
		int provisionalCount = this.RerankCandidates(query, candidateIds[..candidateCountWithSamePartition], rerankHits);

		if ((provisionalCount == rerankHits.Length) &&
			(rerankHits[provisionalCount - 1].Distance < CrossHistoryLowerBoundSquaredL2)) {
			return new HierarchicalSearchTrace(
				parentCount,
				leafCount,
				primaryScanCount,
				candidateCountWithSamePartition,
				maxSelectedLeafSize,
				minSelectedLeafSize,
				SecondaryCandidateScanCount: 0);
		}

		int mergedCandidateCount = candidateCountWithSamePartition;
		mergedCandidateCount = this.SelectTopCandidates(
			quantizedQuery,
			leafIds[..leafCount],
			candidateIds,
			candidateDistances,
			ref mergedCandidateCount,
			ref candidateMaxIndex,
			ref candidateMaxDistance,
			CandidatePartitionSelection.OppositePartitionOnly,
			queryWithoutHistory,
			out int secondaryScanCount,
			out _,
			out _);
		return new HierarchicalSearchTrace(
			parentCount,
			leafCount,
			primaryScanCount + secondaryScanCount,
			mergedCandidateCount,
			maxSelectedLeafSize,
			minSelectedLeafSize,
			secondaryScanCount);
	}

	private int SelectNearestParents(ReadOnlySpan<float> query, Span<int> destinationIds, Span<float> destinationDistances) {
		ReadOnlySpan<float> level1Centroids = this.artifactSet.GetLevel1Centroids();
		int paddedDimension = this.artifactSet.FlatArtifacts.PaddedDimension;
		int count = 0;

		for (int centroidIndex = 0; centroidIndex < this.artifactSet.Level1ClusterCount; centroidIndex++) {
			float distance = DistanceComputations.SquaredL2(
				query[..paddedDimension],
				level1Centroids.Slice(centroidIndex * paddedDimension, paddedDimension));
			InsertSorted(destinationIds, destinationDistances, ref count, centroidIndex, distance);
		}

		return count;
	}

	private int SelectNearestLeaves(
		ReadOnlySpan<float> query,
		ReadOnlySpan<int> selectedParents,
		Span<int> destinationIds,
		Span<float> destinationDistances) {
		ReadOnlySpan<float> leafCentroids = this.artifactSet.GetLeafCentroids();
		int paddedDimension = this.artifactSet.FlatArtifacts.PaddedDimension;
		int count = 0;

		for (int parentIndex = 0; parentIndex < selectedParents.Length; parentIndex++) {
			int parentId = selectedParents[parentIndex];
			int childStart = parentId * this.artifactSet.Level2ClustersPerLevel1;

			for (int childIndex = 0; childIndex < this.artifactSet.Level2ClustersPerLevel1; childIndex++) {
				int leafId = childStart + childIndex;
				float distance = DistanceComputations.SquaredL2(
					query[..paddedDimension],
					leafCentroids.Slice(leafId * paddedDimension, paddedDimension));
				InsertSorted(destinationIds, destinationDistances, ref count, leafId, distance);
			}
		}

		return count;
	}

	private int SelectTopCandidates(
		ReadOnlySpan<sbyte> query,
		ReadOnlySpan<int> selectedLeaves,
		Span<int> destinationIds,
		Span<int> destinationDistances,
		ref int count,
		ref int currentMaxIndex,
		ref int currentMaxDistance,
		CandidatePartitionSelection partitionSelection,
		bool queryWithoutHistory,
		out int scannedCandidateCount,
		out int maxSelectedLeafSize,
		out int minSelectedLeafSize) {
		ReadOnlySpan<int> postingOffsets = this.artifactSet.GetLeafPostingOffsets();
		ReadOnlySpan<byte> quantizedVectors = this.artifactSet.FlatArtifacts.GetQuantizedVectors();
		ReadOnlySpan<int> leafWithoutHistoryCounts = this.artifactSet.HasLastTransactionPartitioning
			? this.artifactSet.GetLeafWithoutHistoryCounts()
			: ReadOnlySpan<int>.Empty;
		int paddedDimension = this.artifactSet.FlatArtifacts.PaddedDimension;
		int leafCount = 0;
		scannedCandidateCount = 0;
		maxSelectedLeafSize = 0;
		minSelectedLeafSize = int.MaxValue;

		for (int leafIndex = 0; leafIndex < selectedLeaves.Length; leafIndex++) {
			int leafId = selectedLeaves[leafIndex];
			int start = postingOffsets[leafId];
			int end = postingOffsets[leafId + 1];

			if ((partitionSelection != CandidatePartitionSelection.All) && this.artifactSet.HasLastTransactionPartitioning) {
				int split = start + leafWithoutHistoryCounts[leafId];

				if (queryWithoutHistory) {
					if (partitionSelection == CandidatePartitionSelection.SamePartitionOnly) {
						end = split;
					} else {
						start = split;
					}
				} else if (partitionSelection == CandidatePartitionSelection.SamePartitionOnly) {
					start = split;
				} else {
					end = split;
				}
			}

			int leafSize = end - start;

			if (leafSize <= 0) {
				continue;
			}

			scannedCandidateCount += leafSize;
			maxSelectedLeafSize = Math.Max(maxSelectedLeafSize, leafSize);
			minSelectedLeafSize = Math.Min(minSelectedLeafSize, leafSize);
			leafCount++;

			if (this.artifactSet.UsesIdentityPostings) {
				ScanIdentityPostingLeaf(
					query,
					quantizedVectors,
					paddedDimension,
					start,
					end,
					destinationIds,
					destinationDistances,
					ref count,
					ref currentMaxIndex,
					ref currentMaxDistance);
			} else {
				ScanExplicitPostingLeaf(
					query,
					this.artifactSet.GetLeafPostingIds(),
					quantizedVectors,
					paddedDimension,
					start,
					end,
					destinationIds,
					destinationDistances,
					ref count,
					ref currentMaxIndex,
					ref currentMaxDistance);
			}
		}

		if (leafCount == 0) {
			minSelectedLeafSize = 0;
		}

		return count;
	}

	private int RerankCandidates(
		ReadOnlySpan<float> query,
		ReadOnlySpan<int> candidateIds,
		Span<SearchHit> destination) {
		ReadOnlySpan<byte> rerankVectors = this.artifactSet.FlatArtifacts.GetRerankVectors();
		int vectorWidthInBytes = checked(this.artifactSet.FlatArtifacts.PaddedDimension * sizeof(ushort));
		int count = 0;

		for (int candidateIndex = 0; candidateIndex < candidateIds.Length; candidateIndex++) {
			int vectorId = candidateIds[candidateIndex];
			float distance = DistanceComputations.SquaredL2F16(
				query,
				rerankVectors.Slice(vectorId * vectorWidthInBytes, vectorWidthInBytes));
			bool isFraud = this.artifactSet.FlatArtifacts.IsFraud(vectorId);
			InsertSorted(destination, ref count, new SearchHit(vectorId, distance, isFraud));
		}

		return count;
	}

	private bool ShouldUseLastTransactionPartitionPruning() =>
		this.useLastTransactionPartitionPruning && this.artifactSet.HasLastTransactionPartitioning;

	private static bool IsWithoutHistoryQuery(ReadOnlySpan<float> query) => (query[5] < 0f) && (query[6] < 0f);

	private static void InsertSorted(Span<SearchHit> destination, ref int count, SearchHit candidate) {
		if ((count == destination.Length) && (candidate.Distance >= destination[destination.Length - 1].Distance)) {
			return;
		}

		int insertAt = Math.Min(count, destination.Length - 1);

		while ((insertAt > 0) && (candidate.Distance < destination[insertAt - 1].Distance)) {
			if (insertAt < destination.Length) {
				destination[insertAt] = destination[insertAt - 1];
			}

			insertAt--;
		}

		destination[insertAt] = candidate;

		if (count < destination.Length) {
			count++;
		}
	}

	private static void InsertSorted(
		Span<int> destinationIds,
		Span<float> destinationDistances,
		ref int count,
		int id,
		float distance) {
		if ((count == destinationIds.Length) && (distance >= destinationDistances[destinationIds.Length - 1])) {
			return;
		}

		int insertAt = Math.Min(count, destinationIds.Length - 1);

		while ((insertAt > 0) && (distance < destinationDistances[insertAt - 1])) {
			if (insertAt < destinationIds.Length) {
				destinationIds[insertAt] = destinationIds[insertAt - 1];
				destinationDistances[insertAt] = destinationDistances[insertAt - 1];
			}

			insertAt--;
		}

		destinationIds[insertAt] = id;
		destinationDistances[insertAt] = distance;

		if (count < destinationIds.Length) {
			count++;
		}
	}

	private static void ScanExplicitPostingLeaf(
		ReadOnlySpan<sbyte> query,
		ReadOnlySpan<int> postingIds,
		ReadOnlySpan<byte> quantizedVectors,
		int paddedDimension,
		int start,
		int end,
		Span<int> destinationIds,
		Span<int> destinationDistances,
		ref int count,
		ref int currentMaxIndex,
		ref int currentMaxDistance) {
		for (int postingIndex = start; postingIndex < end; postingIndex++) {
			int vectorId = postingIds[postingIndex];
			int vectorOffset = checked(vectorId * paddedDimension);
			int distance = DistanceComputations.SquaredL2Q8(
				query,
				quantizedVectors.Slice(vectorOffset, paddedDimension));
			TryInsertCandidate(
				destinationIds,
				destinationDistances,
				ref count,
				ref currentMaxIndex,
				ref currentMaxDistance,
				vectorId,
				distance);
		}
	}

	private static void ScanIdentityPostingLeaf(
		ReadOnlySpan<sbyte> query,
		ReadOnlySpan<byte> quantizedVectors,
		int paddedDimension,
		int start,
		int end,
		Span<int> destinationIds,
		Span<int> destinationDistances,
		ref int count,
		ref int currentMaxIndex,
		ref int currentMaxDistance) {
		int vectorOffset = checked(start * paddedDimension);

		for (int vectorId = start; vectorId < end; vectorId++) {
			int distance = DistanceComputations.SquaredL2Q8(
				query,
				quantizedVectors.Slice(vectorOffset, paddedDimension));
			TryInsertCandidate(
				destinationIds,
				destinationDistances,
				ref count,
				ref currentMaxIndex,
				ref currentMaxDistance,
				vectorId,
				distance);
			vectorOffset += paddedDimension;
		}
	}

	private static void TryInsertCandidate(
		Span<int> destinationIds,
		Span<int> destinationDistances,
		ref int count,
		ref int currentMaxIndex,
		ref int currentMaxDistance,
		int id,
		int distance) {
		if (count < destinationIds.Length) {
			destinationIds[count] = id;
			destinationDistances[count] = distance;

			if ((count == 0) || (distance > currentMaxDistance)) {
				currentMaxDistance = distance;
				currentMaxIndex = count;
			}

			count++;
			return;
		}

		if (distance >= currentMaxDistance) {
			return;
		}

		destinationIds[currentMaxIndex] = id;
		destinationDistances[currentMaxIndex] = distance;
		(currentMaxIndex, currentMaxDistance) = FindMaxCandidate(destinationDistances, count);
	}

	private static (int Index, int Distance) FindMaxCandidate(ReadOnlySpan<int> distances, int count) {
		int maxIndex = 0;
		int maxDistance = distances[0];

		for (int index = 1; index < count; index++) {
			if (distances[index] > maxDistance) {
				maxDistance = distances[index];
				maxIndex = index;
			}
		}

		return (maxIndex, maxDistance);
	}

	private enum CandidatePartitionSelection {
		All = 0,
		SamePartitionOnly = 1,
		OppositePartitionOnly = 2,
	}
}
