using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

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

		if (Avx2.IsSupported && (vector.Length == 16) && (query.Length >= 16)) {
			return SquaredL2Q8Vectorized(query, vector);
		}

		int distance = 0;

		for (int dimension = 0; dimension < vector.Length; dimension++) {
			int difference = query[dimension] - vector[dimension];
			distance += difference * difference;
		}

		return distance;
	}
	private static int SquaredL2Q8Vectorized(ReadOnlySpan<sbyte> query, ReadOnlySpan<sbyte> vector) {
		ref sbyte queryRef = ref MemoryMarshal.GetReference(query);
		ref sbyte vectorRef = ref MemoryMarshal.GetReference(vector);

		Vector128<sbyte> queryBytes = Vector128.LoadUnsafe(ref queryRef);
		Vector128<sbyte> vectorBytes = Vector128.LoadUnsafe(ref vectorRef);
		Vector256<short> queryShorts = Avx2.ConvertToVector256Int16(queryBytes);
		Vector256<short> vectorShorts = Avx2.ConvertToVector256Int16(vectorBytes);
		Vector256<short> diff = Avx2.Subtract(queryShorts, vectorShorts);
		Vector256<int> squares = Avx2.MultiplyAddAdjacent(diff, diff);

		Span<int> lanes = stackalloc int[Vector256<int>.Count];
		squares.CopyTo(lanes);

		int total = 0;

		for (int index = 0; index < lanes.Length; index++) {
			total += lanes[index];
		}

		return total;
	}
}
