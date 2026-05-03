using System.Numerics;
using System.Runtime.InteropServices;

namespace Rinha2026.Core.Search;

public static class DistanceComputations {
	public static float SquaredL2(ReadOnlySpan<float> left, ReadOnlySpan<float> right) {
		if (left.Length != right.Length) {
			throw new ArgumentException("Vector dimensions must match.");
		}

		int index = 0;
		Vector<float> sum = Vector<float>.Zero;

		for (; index <= (left.Length - Vector<float>.Count); index += Vector<float>.Count) {
			Vector<float> leftVector = new(left[index..]);
			Vector<float> rightVector = new(right[index..]);
			Vector<float> difference = leftVector - rightVector;
			sum += difference * difference;
		}

		float total = 0f;

		for (int lane = 0; lane < Vector<float>.Count; lane++) {
			total += sum[lane];
		}

		for (; index < left.Length; index++) {
			float difference = left[index] - right[index];
			total += difference * difference;
		}

		return total;
	}

	public static float SquaredL2F16(ReadOnlySpan<float> query, ReadOnlySpan<byte> encodedVector) {
		ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte, Half>(encodedVector);
		float distance = 0f;

		for (int dimension = 0; dimension < values.Length; dimension++) {
			float difference = query[dimension] - (float)values[dimension];
			distance += difference * difference;
		}

		return distance;
	}

	public static int SquaredL2Q8(ReadOnlySpan<sbyte> query, ReadOnlySpan<byte> encodedVector) {
		ReadOnlySpan<sbyte> vector = MemoryMarshal.Cast<byte, sbyte>(encodedVector);
		int distance = 0;

		for (int dimension = 0; dimension < vector.Length; dimension++) {
			int difference = query[dimension] - vector[dimension];
			distance += difference * difference;
		}

		return distance;
	}
}
