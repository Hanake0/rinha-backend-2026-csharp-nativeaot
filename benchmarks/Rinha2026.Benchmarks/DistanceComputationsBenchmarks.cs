using BenchmarkDotNet.Attributes;

using Rinha2026.Core.Search;

namespace Rinha2026.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class DistanceComputationsBenchmarks {
	private sbyte[] query = default!;
	private byte[] vector = default!;

	[GlobalSetup]
	public void Setup() {
		this.query = [12, -31, 55, -77, 99, -100, 64, -12, 8, 0, -5, 23, 45, -67, 89, -90];
		this.vector = [8, 230, 54, 180, 100, 150, 63, 245, 9, 1, 251, 22, 47, 192, 88, 170];
	}

	[Benchmark(Baseline = true)]
	public int ScalarQ8Distance() {
		int distance = 0;

		for (int index = 0; index < this.query.Length; index++) {
			int difference = this.query[index] - unchecked((sbyte)this.vector[index]);
			distance += difference * difference;
		}

		return distance;
	}

	[Benchmark]
	public int CurrentQ8Distance() => DistanceComputations.SquaredL2Q8(this.query, this.vector);
}
