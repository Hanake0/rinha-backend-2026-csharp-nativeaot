using System.Runtime.InteropServices;

using BenchmarkDotNet.Attributes;

using Rinha2026.Core.Search;

namespace Rinha2026.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class RerankDistanceBenchmarks {
	private static readonly float[] HalfToSingleLookup = CreateHalfToSingleLookup();

	private float[] query = default!;
	private byte[] encodedVector = default!;

	[GlobalSetup]
	public void Setup() {
		this.query = [
			0.13f,
			0.98f,
			0.41f,
			0.77f,
			0.12f,
			-1f,
			-1f,
			0.67f,
			0.31f,
			1f,
			0f,
			1f,
			0.42f,
			0.19f,
			0f,
			0f,
		];
		this.encodedVector = new byte[sizeof(ushort) * 16];

		Half[] source = [
			(Half)0.11f,
			(Half)0.95f,
			(Half)0.39f,
			(Half)0.73f,
			(Half)0.15f,
			(Half)(-1f),
			(Half)(-1f),
			(Half)0.62f,
			(Half)0.35f,
			(Half)1f,
			(Half)0f,
			(Half)1f,
			(Half)0.48f,
			(Half)0.22f,
			(Half)0f,
			(Half)0f,
		];
		source.AsSpan().CopyTo(MemoryMarshal.Cast<byte, Half>(this.encodedVector.AsSpan()));
	}

	[Benchmark(Baseline = true)]
	public float CurrentHalfCastLoop() => CurrentScalar(this.query, this.encodedVector);

	[Benchmark]
	public float LookupLoop() => LookupScalar(this.query, this.encodedVector);

	[Benchmark]
	public float LookupUnrolled16() => LookupScalarUnrolled16(this.query, this.encodedVector);

	[Benchmark]
	public float ProductionDistancePath() => DistanceComputations.SquaredL2F16(this.query, this.encodedVector);

	private static float CurrentScalar(ReadOnlySpan<float> query, ReadOnlySpan<byte> encodedVector) {
		ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte, Half>(encodedVector);
		float distance = 0f;

		for (int dimension = 0; dimension < values.Length; dimension++) {
			float difference = query[dimension] - (float)values[dimension];
			distance += difference * difference;
		}

		return distance;
	}

	private static float LookupScalar(ReadOnlySpan<float> query, ReadOnlySpan<byte> encodedVector) {
		ReadOnlySpan<ushort> values = MemoryMarshal.Cast<byte, ushort>(encodedVector);
		float distance = 0f;

		for (int dimension = 0; dimension < values.Length; dimension++) {
			float difference = query[dimension] - HalfToSingleLookup[values[dimension]];
			distance += difference * difference;
		}

		return distance;
	}

	private static float LookupScalarUnrolled16(ReadOnlySpan<float> query, ReadOnlySpan<byte> encodedVector) {
		ReadOnlySpan<ushort> values = MemoryMarshal.Cast<byte, ushort>(encodedVector);
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
