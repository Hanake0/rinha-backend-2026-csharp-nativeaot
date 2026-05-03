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
