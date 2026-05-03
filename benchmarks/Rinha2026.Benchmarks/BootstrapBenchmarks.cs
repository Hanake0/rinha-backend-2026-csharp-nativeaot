using BenchmarkDotNet.Attributes;

namespace Rinha2026.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class BootstrapBenchmarks {
	[Benchmark]
	public Type GetCoreMarkerType() => typeof(Rinha2026.Core.CoreAssemblyMarker);
}
