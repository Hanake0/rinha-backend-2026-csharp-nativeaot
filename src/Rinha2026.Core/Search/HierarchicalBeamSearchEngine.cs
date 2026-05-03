using Rinha2026.Core.Indexing;

namespace Rinha2026.Core.Search;

public sealed class HierarchicalBeamSearchEngine {
	private const float CrossHistoryLowerBoundSquaredL2 = 2f;

	private readonly HierarchicalArtifactSet artifactSet;
	private readonly bool useLeafRadiusPruning;
	private readonly bool useLastTransactionPartitionPruning;

	public HierarchicalBeamSearchEngine(
		HierarchicalArtifactSet artifactSet,
		bool useLastTransactionPartitionPruning = true,
		bool useLeafRadiusPruning = false) {
		this.artifactSet = artifactSet ?? throw new ArgumentNullException(nameof(artifactSet));
		this.useLeafRadiusPruning = useLeafRadiusPruning;
		this.useLastTransactionPartitionPruning = useLastTransactionPartitionPruning;
	}

	public int CountFraud(
		ReadOnlySpan<float> query,
		int beamLevel1,
		int beamLevel2,
		int rerankCount,
		Span<SearchHit> destination) {
		return this.CountFraud(
			query,
			beamLevel1,
			beamLevel2,
			rerankCount,
			rerankCount,
			minDeniedCount: 0,
			destination);
	}

	public int CountFraud(
		ReadOnlySpan<float> query,
		int beamLevel1,
		int beamLevel2,
		int rerankCount,
		int boundaryRerankCount,
		int minDeniedCount,
		Span<SearchHit> destination) {
		int matchCount = this.Search(
			query,
			beamLevel1,
			beamLevel2,
			rerankCount,
			boundaryRerankCount,
			minDeniedCount,
			destination);
		return CountFraud(destination, matchCount);
	}

	public int Search(
		ReadOnlySpan<float> query,
		int beamLevel1,
		int beamLevel2,
		int rerankCount,
		Span<SearchHit> destination) {
		return this.Search(
			query,
			beamLevel1,
			beamLevel2,
			rerankCount,
			rerankCount,
			minDeniedCount: 0,
			destination);
	}

	public int Search(
		ReadOnlySpan<float> query,
		int beamLevel1,
		int beamLevel2,
		int rerankCount,
		int boundaryRerankCount,
		int minDeniedCount,
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
			Math.Max(rerankCount, boundaryRerankCount),
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
		float candidateMaxDistanceNorm = 0f;

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
				ref candidateMaxDistanceNorm,
				CandidatePartitionSelection.All,
				queryWithoutHistory: false,
				out _,
				out _,
				out _,
				out _,
				out _);
			return this.RerankCandidatesWithBoundaryExpansion(
				query,
				candidateIds[..candidateCount],
				candidateDistances[..candidateCount],
				rerankCount,
				boundaryRerankCount,
				minDeniedCount,
				destination,
				out _);
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
			ref candidateMaxDistanceNorm,
			CandidatePartitionSelection.SamePartitionOnly,
			queryWithoutHistory,
			out _,
			out _,
			out _,
			out _,
			out _);
		int matchCount = this.RerankCandidatesWithBoundaryExpansion(
			query,
			candidateIds[..candidateCountWithSamePartition],
			candidateDistances[..candidateCountWithSamePartition],
			rerankCount,
			boundaryRerankCount,
			minDeniedCount,
			destination,
			out _);

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
			ref candidateMaxDistanceNorm,
			CandidatePartitionSelection.OppositePartitionOnly,
			queryWithoutHistory,
			out _,
			out _,
			out _,
			out _,
			out _);
		return this.RerankCandidatesWithBoundaryExpansion(
			query,
			candidateIds[..mergedCandidateCount],
			candidateDistances[..mergedCandidateCount],
			rerankCount,
			boundaryRerankCount,
			minDeniedCount,
			destination,
			out _);
	}

	public HierarchicalSearchTrace Trace(
		ReadOnlySpan<float> query,
		int beamLevel1,
		int beamLevel2,
		int rerankCount,
		int topK) {
		return this.Trace(
			query,
			beamLevel1,
			beamLevel2,
			rerankCount,
			rerankCount,
			minDeniedCount: 0,
			topK);
	}

	public HierarchicalSearchTrace Trace(
		ReadOnlySpan<float> query,
		int beamLevel1,
		int beamLevel2,
		int rerankCount,
		int boundaryRerankCount,
		int minDeniedCount,
		int topK) {
		FlatArtifactSet flatArtifacts = this.artifactSet.FlatArtifacts;

		if (query.Length < flatArtifacts.PaddedDimension) {
			throw new ArgumentException("Query vector does not match the padded index dimension.", nameof(query));
		}

		int parentBeamCount = Math.Clamp(beamLevel1, 1, this.artifactSet.Level1ClusterCount);
		int leafBeamCount = Math.Clamp(beamLevel2, 1, this.artifactSet.LeafCount);
		int candidateCapacity = Math.Clamp(
			Math.Max(rerankCount, boundaryRerankCount),
			topK,
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
		float candidateMaxDistanceNorm = 0f;

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
				ref candidateMaxDistanceNorm,
				CandidatePartitionSelection.All,
				queryWithoutHistory: false,
				out int scannedCandidateCount,
				out int branchMaxSelectedLeafSize,
				out int branchMinSelectedLeafSize,
				out int prunedLeafCount,
				out int prunedCandidateCount);
			this.RerankCandidatesWithBoundaryExpansion(
				query,
				candidateIds[..candidateCount],
				candidateDistances[..candidateCount],
				rerankCount,
				boundaryRerankCount,
				minDeniedCount,
				rerankHits,
				out int actualRerankCount);
			return new HierarchicalSearchTrace(
				parentCount,
				leafCount,
				scannedCandidateCount,
				actualRerankCount,
				branchMaxSelectedLeafSize,
				branchMinSelectedLeafSize,
				prunedLeafCount,
				prunedCandidateCount,
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
			ref candidateMaxDistanceNorm,
			CandidatePartitionSelection.SamePartitionOnly,
			queryWithoutHistory,
			out int primaryScanCount,
			out int maxSelectedLeafSize,
			out int minSelectedLeafSize,
			out int primaryPrunedLeafCount,
			out int primaryPrunedCandidateCount);
		int provisionalCount = this.RerankCandidatesWithBoundaryExpansion(
			query,
			candidateIds[..candidateCountWithSamePartition],
			candidateDistances[..candidateCountWithSamePartition],
			rerankCount,
			boundaryRerankCount,
			minDeniedCount,
			rerankHits,
			out int primaryRerankCount);

		if ((provisionalCount == rerankHits.Length) &&
			(rerankHits[provisionalCount - 1].Distance < CrossHistoryLowerBoundSquaredL2)) {
			return new HierarchicalSearchTrace(
				parentCount,
				leafCount,
				primaryScanCount,
				primaryRerankCount,
				maxSelectedLeafSize,
				minSelectedLeafSize,
				primaryPrunedLeafCount,
				primaryPrunedCandidateCount,
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
			ref candidateMaxDistanceNorm,
			CandidatePartitionSelection.OppositePartitionOnly,
			queryWithoutHistory,
			out int secondaryScanCount,
			out _,
			out _,
			out int secondaryPrunedLeafCount,
			out int secondaryPrunedCandidateCount);
		this.RerankCandidatesWithBoundaryExpansion(
			query,
			candidateIds[..mergedCandidateCount],
			candidateDistances[..mergedCandidateCount],
			rerankCount,
			boundaryRerankCount,
			minDeniedCount,
			rerankHits,
			out int finalRerankCount);
		return new HierarchicalSearchTrace(
			parentCount,
			leafCount,
			primaryScanCount + secondaryScanCount,
			finalRerankCount,
			maxSelectedLeafSize,
			minSelectedLeafSize,
			primaryPrunedLeafCount + secondaryPrunedLeafCount,
			primaryPrunedCandidateCount + secondaryPrunedCandidateCount,
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
		ref float currentMaxDistanceNorm,
		CandidatePartitionSelection partitionSelection,
		bool queryWithoutHistory,
		out int scannedCandidateCount,
		out int maxSelectedLeafSize,
		out int minSelectedLeafSize,
		out int prunedLeafCount,
		out int prunedCandidateCount) {
		ReadOnlySpan<int> postingOffsets = this.artifactSet.GetLeafPostingOffsets();
		ReadOnlySpan<byte> quantizedVectors = this.artifactSet.FlatArtifacts.GetQuantizedVectors();
		ReadOnlySpan<int> leafWithoutHistoryCounts = this.artifactSet.HasLastTransactionPartitioning
			? this.artifactSet.GetLeafWithoutHistoryCounts()
			: ReadOnlySpan<int>.Empty;
		bool enableLeafRadiusPruning = this.ShouldUseLeafRadiusPruning();
		ReadOnlySpan<sbyte> quantizedLeafCentroids = enableLeafRadiusPruning
			? this.artifactSet.GetQuantizedLeafCentroids()
			: ReadOnlySpan<sbyte>.Empty;
		ReadOnlySpan<float> leafRadiusBounds = enableLeafRadiusPruning
			? this.artifactSet.GetLeafRadiusBounds()
			: ReadOnlySpan<float>.Empty;
		int paddedDimension = this.artifactSet.FlatArtifacts.PaddedDimension;
		int leafCount = 0;
		scannedCandidateCount = 0;
		maxSelectedLeafSize = 0;
		minSelectedLeafSize = int.MaxValue;
		prunedLeafCount = 0;
		prunedCandidateCount = 0;

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

			if (enableLeafRadiusPruning &&
				ShouldPruneLeaf(
					query,
					quantizedLeafCentroids,
					leafRadiusBounds,
					paddedDimension,
					leafId,
					destinationIds.Length,
					count,
					currentMaxDistance,
					currentMaxDistanceNorm)) {
				prunedLeafCount++;
				prunedCandidateCount += leafSize;
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
					ref currentMaxDistance,
					ref currentMaxDistanceNorm);
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
					ref currentMaxDistance,
					ref currentMaxDistanceNorm);
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
		return this.RerankCandidates(query, candidateIds, destination, count: 0);
	}

	private int RerankCandidates(
		ReadOnlySpan<float> query,
		ReadOnlySpan<int> candidateIds,
		Span<SearchHit> destination,
		int count) {
		int matchCount = count;

		if (this.artifactSet.FlatArtifacts.HasFullPrecisionRerankVectors) {
			ReadOnlySpan<byte> rerankVectors = this.artifactSet.FlatArtifacts.GetFullPrecisionRerankVectors();
			int vectorWidthInBytes = checked(this.artifactSet.FlatArtifacts.PaddedDimension * sizeof(float));

			for (int candidateIndex = 0; candidateIndex < candidateIds.Length; candidateIndex++) {
				int vectorId = candidateIds[candidateIndex];
				float distance = DistanceComputations.SquaredL2F32(
					query,
					rerankVectors.Slice(vectorId * vectorWidthInBytes, vectorWidthInBytes));
				bool isFraud = this.artifactSet.FlatArtifacts.IsFraud(vectorId);
				InsertSorted(destination, ref matchCount, new SearchHit(vectorId, distance, isFraud), this.artifactSet.FlatArtifacts);
			}

			return matchCount;
		}

		ReadOnlySpan<byte> fallbackRerankVectors = this.artifactSet.FlatArtifacts.GetRerankVectors();
		int fallbackVectorWidthInBytes = checked(this.artifactSet.FlatArtifacts.PaddedDimension * sizeof(ushort));

		for (int candidateIndex = 0; candidateIndex < candidateIds.Length; candidateIndex++) {
			int vectorId = candidateIds[candidateIndex];
			float distance = DistanceComputations.SquaredL2F16(
				query,
				fallbackRerankVectors.Slice(vectorId * fallbackVectorWidthInBytes, fallbackVectorWidthInBytes));
			bool isFraud = this.artifactSet.FlatArtifacts.IsFraud(vectorId);
			InsertSorted(destination, ref matchCount, new SearchHit(vectorId, distance, isFraud), this.artifactSet.FlatArtifacts);
		}

		return matchCount;
	}

	private int RerankCandidatesWithBoundaryExpansion(
		ReadOnlySpan<float> query,
		Span<int> candidateIds,
		Span<int> candidateDistances,
		int rerankCount,
		int boundaryRerankCount,
		int minDeniedCount,
		Span<SearchHit> destination,
		out int actualRerankCount) {
		if (candidateIds.Length != candidateDistances.Length) {
			throw new ArgumentException("Candidate ids and distances must have matching lengths.");
		}

		int primaryRerankCount = ClampRerankCount(rerankCount, destination.Length, candidateIds.Length);
		int expandedRerankCount = ClampRerankCount(boundaryRerankCount, destination.Length, candidateIds.Length);
		int matchCount;

		if (expandedRerankCount > primaryRerankCount) {
			Span<int> primaryCandidateIds = primaryRerankCount <= 64
				? stackalloc int[primaryRerankCount]
				: new int[primaryRerankCount];
			Span<int> primaryCandidateDistances = primaryRerankCount <= 64
				? stackalloc int[primaryRerankCount]
				: new int[primaryRerankCount];
			int primaryCandidateCount = 0;
			int currentMaxIndex = 0;
			int currentMaxDistance = int.MinValue;
			float currentMaxDistanceNorm = 0f;

			for (int candidateIndex = 0; candidateIndex < expandedRerankCount; candidateIndex++) {
				TryInsertCandidate(
					primaryCandidateIds,
					primaryCandidateDistances,
					ref primaryCandidateCount,
					ref currentMaxIndex,
					ref currentMaxDistance,
					ref currentMaxDistanceNorm,
					candidateIds[candidateIndex],
					candidateDistances[candidateIndex]);
			}

			matchCount = this.RerankCandidates(query, primaryCandidateIds[..primaryCandidateCount], destination);
		} else {
			matchCount = this.RerankCandidates(query, candidateIds[..primaryRerankCount], destination);
		}

		actualRerankCount = primaryRerankCount;

		if ((expandedRerankCount > primaryRerankCount) &&
			ShouldExpandBoundaryRerank(CountFraud(destination, matchCount), destination.Length, minDeniedCount)) {
			matchCount = this.RerankCandidates(query, candidateIds[..expandedRerankCount], destination);
			actualRerankCount = expandedRerankCount;
		}

		return matchCount;
	}

	private bool ShouldUseLastTransactionPartitionPruning() =>
		this.useLastTransactionPartitionPruning && this.artifactSet.HasLastTransactionPartitioning;

	private bool ShouldUseLeafRadiusPruning() =>
		this.useLeafRadiusPruning && this.artifactSet.HasLeafRadiusBounds;

	private static bool IsWithoutHistoryQuery(ReadOnlySpan<float> query) => (query[5] < 0f) && (query[6] < 0f);

	private static int ClampRerankCount(int requestedCount, int topK, int candidateCount) =>
		Math.Min(candidateCount, Math.Max(requestedCount, topK));

	private static int CountFraud(ReadOnlySpan<SearchHit> hits, int count) {
		int fraudCount = 0;

		for (int index = 0; index < count; index++) {
			if (hits[index].IsFraud) {
				fraudCount++;
			}
		}

		return fraudCount;
	}

	private static bool ShouldExpandBoundaryRerank(int fraudCount, int topK, int minDeniedCount) {
		if ((topK <= 0) || (minDeniedCount < 0)) {
			return false;
		}

		return (fraudCount == minDeniedCount) || (fraudCount == (minDeniedCount - 1));
	}

	private static void InsertSorted(Span<SearchHit> destination, ref int count, SearchHit candidate, FlatArtifactSet artifactSet) {
		if ((count == destination.Length) && !ShouldInsertBefore(candidate, destination[destination.Length - 1], artifactSet)) {
			return;
		}

		int insertAt = Math.Min(count, destination.Length - 1);

		while ((insertAt > 0) && ShouldInsertBefore(candidate, destination[insertAt - 1], artifactSet)) {
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

	private static bool ShouldInsertBefore(SearchHit candidate, SearchHit existing, FlatArtifactSet artifactSet) {
		if (candidate.Distance < existing.Distance) {
			return true;
		}

		if (candidate.Distance > existing.Distance) {
			return false;
		}

		return artifactSet.GetStableOrderKey(candidate.Index) < artifactSet.GetStableOrderKey(existing.Index);
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
		ref int currentMaxDistance,
		ref float currentMaxDistanceNorm) {
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
				ref currentMaxDistanceNorm,
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
		ref int currentMaxDistance,
		ref float currentMaxDistanceNorm) {
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
				ref currentMaxDistanceNorm,
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
		ref float currentMaxDistanceNorm,
		int id,
		int distance) {
		if (count < destinationIds.Length) {
			destinationIds[count] = id;
			destinationDistances[count] = distance;

			if ((count == 0) || (distance > currentMaxDistance)) {
				currentMaxDistance = distance;
				currentMaxIndex = count;
				currentMaxDistanceNorm = MathF.Sqrt(distance);
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
		currentMaxDistanceNorm = MathF.Sqrt(currentMaxDistance);
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

	private static bool ShouldPruneLeaf(
		ReadOnlySpan<sbyte> query,
		ReadOnlySpan<sbyte> quantizedLeafCentroids,
		ReadOnlySpan<float> leafRadiusBounds,
		int paddedDimension,
		int leafId,
		int candidateCapacity,
		int count,
		int currentMaxDistance,
		float currentMaxDistanceNorm) {
		if ((count < candidateCapacity) || (currentMaxDistance < 0)) {
			return false;
		}

		int centroidOffset = checked(leafId * paddedDimension);
		int centroidDistance = DistanceComputations.SquaredL2Q8(
			query,
			quantizedLeafCentroids.Slice(centroidOffset, paddedDimension));
		float radius = leafRadiusBounds[leafId];
		float threshold = currentMaxDistance + (2f * radius * currentMaxDistanceNorm) + (radius * radius);
		return centroidDistance > threshold;
	}

	private enum CandidatePartitionSelection {
		All = 0,
		SamePartitionOnly = 1,
		OppositePartitionOnly = 2,
	}
}
