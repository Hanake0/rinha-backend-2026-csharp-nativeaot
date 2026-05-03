# Rinha de Backend 2026 - C# NativeAOT

`C#` / `.NET 10` / `NativeAOT` submission workspace for the 2026 Rinha de Backend.

## Current intent

- target `0.5 ms` from day one;
- stay compliant with `1 lb + 2 api` services;
- keep the runtime self-contained and configurable;
- validate every performance claim with constrained Docker Compose, not only host-native micro-benchmarks.

Planning documents live outside this repo in [`../plans`](../plans/).

## Current default stack

- load balancer: `nginx:1.27-alpine`
- API runtime: `.NET 10 NativeAOT`
- dataset artifact root: `./runtime-data`
- search mode: `HierarchicalBeamIvf`
- default search settings:
  - `beamLevel1 = 10`
  - `beamLevel2 = 32`
  - `rerankCount = 48`
  - `topK = 5`
  - `approvalThreshold = 0.6`
- constrained runtime split:
  - `lb = 0.20 CPU / 48 MB`
  - `api1 = 0.40 CPU / 151 MB`
  - `api2 = 0.40 CPU / 151 MB`

## Workflow

Work is expected to move in this loop:

1. code
2. format
3. test
4. benchmark
5. commit

Commit style is `gitmoji + conventional commits`. See [`CONTRIBUTING.md`](./CONTRIBUTING.md).

## Repository map

- `src/`
  - `Rinha2026.Api`: NativeAOT HTTP service
  - `Rinha2026.Core`: parsing, vectorization, indexing, search
  - `Rinha2026.IndexBuilder`: offline artifact builder
- `tests/`
  - unit and parity coverage for parser, search, runtime configuration, and endpoint behavior
- `benchmarks/`
  - BenchmarkDotNet micro-benchmarks
  - committed benchmark summaries
- `tools/`
  - `Rinha2026.Evaluator`: offline official-corpus evaluator with latency breakdown
  - `Rinha2026.HttpReplay`: request replay tool for live parity checks
- `scripts/`
  - repeatable local workflow for artifact prep, evaluation, and constrained stack benchmarks
- `runtime-data/`
  - current tracked runtime artifact set used by the default compose stack

## Commands

Build:

```powershell
dotnet build Rinha2026.NativeAot.sln -c Release
```

Test:

```powershell
dotnet test Rinha2026.NativeAot.sln -c Release --verbosity minimal
```

Format:

```powershell
dotnet format Rinha2026.NativeAot.sln --verify-no-changes
```

Micro-benchmarks:

```powershell
dotnet run -c Release --project .\benchmarks\Rinha2026.Benchmarks\Rinha2026.Benchmarks.csproj -- --filter "*"
```

Official corpus evaluator:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\evaluate-official.ps1 `
  -ParseMode ServiceManual `
  -BeamLevel1 10 `
  -BeamLevel2 32 `
  -RerankCount 48 `
  -TopK 5 `
  -ApprovalThreshold 0.6
```

Evaluator grid sweep:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\tune-evaluator-grid.ps1 `
  -ParseMode ServiceManual `
  -BeamLevel1Values 6,8,10 `
  -BeamLevel2Values 24,32 `
  -RerankCountValues 48,64
```

Constrained full-stack benchmark:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\benchmark-official-compose.ps1 `
  -BeamLevel1 10 `
  -BeamLevel2 32 `
  -RerankCount 48 `
  -TopK 5 `
  -ApprovalThreshold 0.6 `
  -HttpParserMode Manual `
  -HttpIoQueueCount 0 `
  -HttpInlineScheduling true `
  -HttpNoDelay true `
  -LbCpus 0.20 `
  -ApiCpus 0.40 `
  -LbMemLimit 48m `
  -ApiMemLimit 151m
```

## Benchmark layers

Use the benchmark stack in this order:

1. `BenchmarkDotNet` micro-benchmarks for parser, vectorization, encoding, and search kernels
2. `Rinha2026.Evaluator` for full official-corpus correctness and service-path latency
3. constrained `docker compose` + official `k6` workload for final stack validation

The current status and latest validated numbers live in [`STATUS.md`](./STATUS.md).
