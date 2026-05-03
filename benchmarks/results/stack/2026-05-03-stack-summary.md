# 2026-05-03 Stack Summary

## Scope

This file records the validated stack-level results after:

- topology comparison at fixed training budget
- constrained compose validation of the `256x128` topology frontier
- q8 leaf-radius pruning
- request-stage profiling on the pruning-enabled artifact family
- LB/API CPU split tuning

## Chosen default

- compose: `docker-compose.yml`
- load balancer: `nginx:1.27-alpine`
- dataset artifact: `runtime-data/`
- search settings:
  - `beamLevel1 = 8`
  - `beamLevel2 = 128`
  - `rerankCount = 48`
  - `topK = 5`
  - `approvalThreshold = 0.6`
  - `useLeafRadiusPruning = true`
  - `useLastTransactionPartitionPruning = true`
- transport settings:
  - `Runtime:Http:IoQueueCount = 0`
  - `Runtime:Http:UnsafePreferInlineScheduling = true`
  - `Runtime:Http:NoDelay = true`
- resource split:
  - `lb = 0.15 CPU / 48 MB`
  - `api1 = 0.425 CPU / 151 MB`
  - `api2 = 0.425 CPU / 151 MB`

## Artifact provenance

- source build directory promoted into `runtime-data/`: `runtime-data-256x128-radii-s524k`
- hierarchical training:
  - `L1 = 256`
  - `L2 per L1 = 128`
  - training sample size `524,288`
  - k-means iterations `12`
- extra metadata:
  - `leaf.radius.q8.f32.bin`

## Full-corpus evaluator

Command shape:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\evaluate-official.ps1 `
  -ParseMode ServiceManual `
  -IndexKind HierarchicalBeamIvf `
  -BeamLevel1 8 `
  -BeamLevel2 128 `
  -RerankCount 48 `
  -TopK 5 `
  -ApprovalThreshold 0.6 `
  -UseLeafRadiusPruning true
```

Validated result:

- `FP = 1`
- `FN = 2`
- detection score `2729.07`
- search latency:
  - latest validated `ServiceManual`: `p50 = 104.0 us`, `p95 = 214.1 us`, `p99 = 505.1 us`, `mean = 117.0 us`
  - best observed `ManualParser` frontier: `p50 = 104.1 us`, `p95 = 210.2 us`, `p99 = 390.8 us`, `mean = 114.4 us`

## Full compliant stack

Command:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\benchmark-official-compose.ps1 `
  -RuntimeDataDir runtime-data `
  -BeamLevel1 8 `
  -BeamLevel2 128 `
  -RerankCount 48 `
  -TopK 5 `
  -ApprovalThreshold 0.6 `
  -UseLeafRadiusPruning true `
  -HttpParserMode Manual `
  -HttpIoQueueCount 0 `
  -HttpInlineScheduling true `
  -HttpNoDelay true `
  -LbCpus 0.15 `
  -ApiCpus 0.425 `
  -LbMemLimit 48m `
  -ApiMemLimit 151m
```

Validated result:

- `p99 = 1.13 ms`
- `FP = 1`
- `FN = 2`
- `http_errors = 0`
- final score `5675.38`

Best observed result on the current chosen default:

- `p99 = 1.10 ms`
- `FP = 1`
- `FN = 2`
- `http_errors = 0`
- final score `5688.47`

## Comparison notes

- `256x128` still dominates the earlier `512x64` and `1024x32` families on scan-count efficiency at the same training budget.
- q8 leaf-radius pruning made the wider leaf frontier viable under the real stack.
- On the pruning-enabled artifact family:
  - `8/72/48` reached `p99 = 1.14 ms`, `FP = 2`, `FN = 5`, `final = 5567.40`
  - `8/80/48` reached `p99 = 1.09 ms`, `FP = 1`, `FN = 4`, `final = 5618.90`
  - `8/96/48` reached `p99 = 1.10 ms`, `FP = 1`, `FN = 3`, `final = 5645.94`
  - `8/128/48` reached `p99 = 1.10 ms`, `FP = 1`, `FN = 2`, `final = 5688.47`
- Lowering the LB split from `0.20` to `0.15` remained beneficial after the pruning win.

## Remaining gap

Current default stack is:

- compliant
- self-contained
- correctness-stable
- benchmarkable with one command

But it is not yet at the target:

- current best observed final score: `5688.47`
- target score: `6000`
- current best observed full-stack p99: `1.10 ms`
- target p99: `<= 0.5 ms`
- current best correctness gap: `1 FP / 2 FN`

The next optimization frontier is no longer basic parser/runtime cost. It is the combination of:

1. closing the last correctness gap without inflating search tail
2. shaving the remaining stack-visible transport overhead
