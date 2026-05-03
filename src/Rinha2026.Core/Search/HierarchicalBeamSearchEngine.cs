using Rinha2026.Core.Indexing;

namespace Rinha2026.Core.Search;

public sealed class HierarchicalBeamSearchEngine {
	private readonly HierarchicalArtifactSet artifactSet;

	public HierarchicalBeamSearchEngine(HierarchicalArtifactSet artifactSet) {
		this.artifactSet = artifactSet ?? throw new ArgumentNullException(nameof(artifactSet));
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

		VectorEncoding.EncodeQ8Symmetric(query[..flatArtifacts.PaddedDimension], quantizedQuery);

		int parentCount = this.SelectNearestParents(query, parentIds, parentDistances);
		int leafCount = this.SelectNearestLeaves(query, parentIds[..parentCount], leafIds, leafDistances);
		int candidateCount = this.SelectTopCandidates(quantizedQuery, leafIds[..leafCount], candidateIds, candidateDistances);
		return this.RerankCandidates(query, candidateIds[..candidateCount], destination);
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
		Span<int> destinationDistances) {
		ReadOnlySpan<int> postingOffsets = this.artifactSet.GetLeafPostingOffsets();
		ReadOnlySpan<int> postingIds = this.artifactSet.GetLeafPostingIds();
		ReadOnlySpan<byte> quantizedVectors = this.artifactSet.FlatArtifacts.GetQuantizedVectors();
		int paddedDimension = this.artifactSet.FlatArtifacts.PaddedDimension;
		int count = 0;

		for (int leafIndex = 0; leafIndex < selectedLeaves.Length; leafIndex++) {
			int leafId = selectedLeaves[leafIndex];
			int start = postingOffsets[leafId];
			int end = postingOffsets[leafId + 1];

			for (int postingIndex = start; postingIndex < end; postingIndex++) {
				int vectorId = postingIds[postingIndex];
				int vectorOffset = checked(vectorId * paddedDimension);
				int distance = DistanceComputations.SquaredL2Q8(
					query,
					quantizedVectors.Slice(vectorOffset, paddedDimension));
				InsertSorted(destinationIds, destinationDistances, ref count, vectorId, distance);
			}
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

	private static void InsertSorted(
		Span<int> destinationIds,
		Span<int> destinationDistances,
		ref int count,
		int id,
		int distance) {
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
}
