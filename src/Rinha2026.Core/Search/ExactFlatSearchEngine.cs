using Rinha2026.Core.Indexing;

namespace Rinha2026.Core.Search;

public sealed class ExactFlatSearchEngine {
	private readonly FlatArtifactSet artifactSet;

	public ExactFlatSearchEngine(FlatArtifactSet artifactSet) {
		this.artifactSet = artifactSet ?? throw new ArgumentNullException(nameof(artifactSet));
	}

	public int CountFraud(ReadOnlySpan<float> query, Span<SearchHit> destination) {
		int matchCount = this.Search(query, destination);
		int fraudCount = 0;

		for (int index = 0; index < matchCount; index++) {
			if (destination[index].IsFraud) {
				fraudCount++;
			}
		}

		return fraudCount;
	}

	public int Search(ReadOnlySpan<float> query, Span<SearchHit> destination) {
		if (destination.IsEmpty) {
			throw new ArgumentException("Destination span must not be empty.", nameof(destination));
		}

		if (query.Length < this.artifactSet.PaddedDimension) {
			throw new ArgumentException("Query vector does not match the padded index dimension.", nameof(query));
		}

		int count = 0;

		if (this.artifactSet.HasFullPrecisionRerankVectors) {
			ReadOnlySpan<byte> rerankVectors = this.artifactSet.GetFullPrecisionRerankVectors();
			int vectorWidthInBytes = checked(this.artifactSet.PaddedDimension * sizeof(float));

			for (int vectorIndex = 0; vectorIndex < this.artifactSet.VectorCount; vectorIndex++) {
				int vectorOffset = checked(vectorIndex * vectorWidthInBytes);
				float distance = DistanceComputations.SquaredL2F32(
					query,
					rerankVectors.Slice(vectorOffset, vectorWidthInBytes));
				bool isFraud = this.artifactSet.IsFraud(vectorIndex);
				InsertSorted(destination, ref count, new SearchHit(vectorIndex, distance, isFraud), this.artifactSet);
			}

			return count;
		}

		ReadOnlySpan<byte> fallbackRerankVectors = this.artifactSet.GetRerankVectors();
		int fallbackVectorWidthInBytes = checked(this.artifactSet.PaddedDimension * sizeof(ushort));

		for (int vectorIndex = 0; vectorIndex < this.artifactSet.VectorCount; vectorIndex++) {
			int vectorOffset = checked(vectorIndex * fallbackVectorWidthInBytes);
			float distance = DistanceComputations.SquaredL2F16(
				query,
				fallbackRerankVectors.Slice(vectorOffset, fallbackVectorWidthInBytes));
			bool isFraud = this.artifactSet.IsFraud(vectorIndex);
			InsertSorted(destination, ref count, new SearchHit(vectorIndex, distance, isFraud), this.artifactSet);
		}

		return count;
	}

	private static void InsertSorted(Span<SearchHit> destination, ref int count, SearchHit candidate, FlatArtifactSet artifactSet) {
		int length = destination.Length;

		if ((count == length) && !ShouldInsertBefore(candidate, destination[length - 1], artifactSet)) {
			return;
		}

		int insertAt = Math.Min(count, length - 1);

		while ((insertAt > 0) && ShouldInsertBefore(candidate, destination[insertAt - 1], artifactSet)) {
			if (insertAt < length) {
				destination[insertAt] = destination[insertAt - 1];
			}

			insertAt--;
		}

		destination[insertAt] = candidate;

		if (count < length) {
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
}
