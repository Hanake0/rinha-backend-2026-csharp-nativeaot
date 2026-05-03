using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Rinha2026.Benchmarks.BootstrapBenchmarks).Assembly).Run(args);
