using BenchmarkDotNet.Attributes;

using Rinha2026.Core.Indexing;

namespace Rinha2026.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class VectorEncodingBenchmarks {
	private byte[] f16Destination = default!;
	private byte[] q8Destination = default!;
	private float[] vector = default!;

	[GlobalSetup]
	public void Setup() {
		this.vector = [0.9506f, 0.8333f, 1.0f, 0.2174f, 0.8333f, -1f, -1f, 0.9523f, 1.0f, 0f, 1f, 1f, 0.75f, 0.0055f, 0f, 0f];
		this.q8Destination = new byte[16];
		this.f16Destination = new byte[32];
	}

	[Benchmark]
	public byte EncodeQ8() {
		VectorEncoding.EncodeQ8Symmetric(this.vector, this.q8Destination);
		return this.q8Destination[0];
	}

	[Benchmark]
	public byte EncodeFloat16() {
		VectorEncoding.EncodeFloat16(this.vector, this.f16Destination);
		return this.f16Destination[0];
	}
}
