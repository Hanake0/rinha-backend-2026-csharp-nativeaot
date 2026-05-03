using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Rinha2026.Core.Search;

public static class DistanceComputations {
	private static readonly float[] HalfToSingleLookup = CreateHalfToSingleLookup();

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
		ReadOnlySpan<ushort> values = MemoryMarshal.Cast<byte, ushort>(encodedVector);

		if (query.Length < values.Length) {
			throw new ArgumentException("Vector dimensions must match.");
		}

		if (values.Length == 16) {
			return SquaredL2F16Fixed16(query, values);
		}

		float distance = 0f;

		for (int dimension = 0; dimension < values.Length; dimension++) {
			float difference = query[dimension] - HalfToSingleLookup[values[dimension]];
			distance += difference * difference;
		}

		return distance;
	}

	public static int SquaredL2Q8(ReadOnlySpan<sbyte> query, ReadOnlySpan<byte> encodedVector) {
		ReadOnlySpan<sbyte> vector = MemoryMarshal.Cast<byte, sbyte>(encodedVector);
		return SquaredL2Q8(query, vector);
	}

	public static int SquaredL2Q8(ReadOnlySpan<sbyte> query, ReadOnlySpan<sbyte> vector) {
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

	private static float SquaredL2F16Fixed16(ReadOnlySpan<float> query, ReadOnlySpan<ushort> values) {
		float d0 = query[0] - HalfToSingleLookup[values[0]];
		float d1 = query[1] - HalfToSingleLookup[values[1]];
		float d2 = query[2] - HalfToSingleLookup[values[2]];
		float d3 = query[3] - HalfToSingleLookup[values[3]];
		float d4 = query[4] - HalfToSingleLookup[values[4]];
		float d5 = query[5] - HalfToSingleLookup[values[5]];
		float d6 = query[6] - HalfToSingleLookup[values[6]];
		float d7 = query[7] - HalfToSingleLookup[values[7]];
		float d8 = query[8] - HalfToSingleLookup[values[8]];
		float d9 = query[9] - HalfToSingleLookup[values[9]];
		float d10 = query[10] - HalfToSingleLookup[values[10]];
		float d11 = query[11] - HalfToSingleLookup[values[11]];
		float d12 = query[12] - HalfToSingleLookup[values[12]];
		float d13 = query[13] - HalfToSingleLookup[values[13]];
		float d14 = query[14] - HalfToSingleLookup[values[14]];
		float d15 = query[15] - HalfToSingleLookup[values[15]];

		return
			(d0 * d0) +
			(d1 * d1) +
			(d2 * d2) +
			(d3 * d3) +
			(d4 * d4) +
			(d5 * d5) +
			(d6 * d6) +
			(d7 * d7) +
			(d8 * d8) +
			(d9 * d9) +
			(d10 * d10) +
			(d11 * d11) +
			(d12 * d12) +
			(d13 * d13) +
			(d14 * d14) +
			(d15 * d15);
	}

	private static float[] CreateHalfToSingleLookup() {
		float[] lookup = new float[ushort.MaxValue + 1];

		for (int value = 0; value < lookup.Length; value++) {
			lookup[value] = (float)BitConverter.UInt16BitsToHalf((ushort)value);
		}

		return lookup;
	}
}
