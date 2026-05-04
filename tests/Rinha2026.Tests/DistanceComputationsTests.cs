using System.Runtime.InteropServices;

using Rinha2026.Core.Search;

namespace Rinha2026.Tests;

public sealed class DistanceComputationsTests {
	[Fact]
	public void SquaredL2Q8MatchesScalarReference() {
		sbyte[] query = [12, -31, 55, -77, 99, -100, 64, -12, 8, 0, -5, 23, 45, -67, 89, -90];
		byte[] encodedVector = [8, 230, 54, 180, 100, 150, 63, 245, 9, 1, 251, 22, 47, 192, 88, 170];

		int actual = DistanceComputations.SquaredL2Q8(query, encodedVector);
		int expected = 0;

		for (int index = 0; index < query.Length; index++) {
			int difference = query[index] - unchecked((sbyte)encodedVector[index]);
			expected += difference * difference;
		}

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void SquaredL2Q8MatchesScalarReferenceForLongerVectors() {
		sbyte[] query = [12, -31, 55, -77, 99, -100, 64, -12, 8, 0, -5, 23, 45, -67, 89, -90, 14, -29, 53, -75, 97, -98, 62, -10];
		sbyte[] vector = [8, -26, 54, -76, 100, -106, 63, -11, 9, 1, -5, 22, 47, -64, 88, -86, 13, -27, 50, -73, 101, -95, 60, -12];

		int actual = DistanceComputations.SquaredL2Q8(query, vector);
		int expected = 0;

		for (int index = 0; index < vector.Length; index++) {
			int difference = query[index] - vector[index];
			expected += difference * difference;
		}

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void SquaredL2F32MatchesScalarReference() {
		float[] query = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f];
		float[] vector = [0.0f, 0.2f, 0.35f, 0.39f, 0.5f, 0.58f, 0.71f, 0.79f, 0.88f, 1.02f, 1.09f, 1.18f, 1.27f, 1.38f, 1.52f, 1.59f];
		byte[] encodedVector = MemoryMarshal.AsBytes<float>(vector).ToArray();

		float actual = DistanceComputations.SquaredL2F32(query, encodedVector);
		float expected = 0f;

		for (int index = 0; index < vector.Length; index++) {
			float difference = query[index] - vector[index];
			expected += difference * difference;
		}

		Assert.Equal(expected, actual, 5);
	}

	[Fact]
	public void SquaredL2F16MatchesScalarReference() {
		float[] query = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f];
		Half[] values = [(Half)0.0f, (Half)0.2f, (Half)0.35f, (Half)0.39f, (Half)0.5f, (Half)0.58f, (Half)0.71f, (Half)0.79f, (Half)0.88f, (Half)1.02f, (Half)1.09f, (Half)1.18f, (Half)1.27f, (Half)1.38f, (Half)1.52f, (Half)1.59f];
		byte[] encodedVector = MemoryMarshal.AsBytes<Half>(values).ToArray();

		float actual = DistanceComputations.SquaredL2F16(query, encodedVector);
		float expected = 0f;

		for (int index = 0; index < values.Length; index++) {
			float difference = query[index] - (float)values[index];
			expected += difference * difference;
		}

		Assert.Equal(expected, actual, 5);
	}
}
