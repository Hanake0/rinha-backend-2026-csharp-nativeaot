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

		ReadOnlySpan<byte> rerankVectors = this.artifactSet.GetRerankVectors();
		int vectorWidthInBytes = checked(this.artifactSet.PaddedDimension * sizeof(ushort));
		int count = 0;

		for (int vectorIndex = 0; vectorIndex < this.artifactSet.VectorCount; vectorIndex++) {
			int vectorOffset = checked(vectorIndex * vectorWidthInBytes);
			float distance = ComputeSquaredL2(
				query,
				rerankVectors.Slice(vectorOffset, vectorWidthInBytes));
			bool isFraud = this.artifactSet.IsFraud(vectorIndex);
			InsertSorted(destination, ref count, new SearchHit(vectorIndex, distance, isFraud));
		}

		return count;
	}

	private static float ComputeSquaredL2(ReadOnlySpan<float> query, ReadOnlySpan<byte> encodedVector) {
		return DistanceComputations.SquaredL2F16(query, encodedVector);
	}

	private static void InsertSorted(Span<SearchHit> destination, ref int count, SearchHit candidate) {
		int length = destination.Length;

		if ((count == length) && (candidate.Distance >= destination[length - 1].Distance)) {
			return;
		}

		int insertAt = Math.Min(count, length - 1);

		while ((insertAt > 0) && (candidate.Distance < destination[insertAt - 1].Distance)) {
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
}
