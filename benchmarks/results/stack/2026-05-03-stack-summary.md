# 2026-05-03 Stack Summary

## Scope

This file records the validated stack-level results after:

- topology comparison at fixed training budget
- constrained compose validation of the new topology frontier
- request-stage profiling on the promoted baseline
- LB/API CPU split tuning

## Chosen default

- compose: `docker-compose.yml`
- load balancer: `nginx:1.27-alpine`
- dataset artifact: `runtime-data/`
- search settings:
  - `beamLevel1 = 8`
  - `beamLevel2 = 72`
  - `rerankCount = 48`
  - `topK = 5`
  - `approvalThreshold = 0.6`
- transport settings:
  - `Runtime:Http:IoQueueCount = 0`
  - `Runtime:Http:UnsafePreferInlineScheduling = true`
  - `Runtime:Http:NoDelay = true`
- resource split:
  - `lb = 0.15 CPU / 48 MB`
  - `api1 = 0.425 CPU / 151 MB`
  - `api2 = 0.425 CPU / 151 MB`

## Artifact provenance

- source build directory promoted into `runtime-data/`: `runtime-data-256x128-s524k`
- hierarchical training:
  - `L1 = 256`
  - `L2 per L1 = 128`
  - training sample size `524,288`
  - k-means iterations `12`

## Full-corpus evaluator

Command shape:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\evaluate-official.ps1 `
  -ParseMode ServiceManual `
  -IndexKind HierarchicalBeamIvf `
  -BeamLevel1 8 `
  -BeamLevel2 72 `
  -RerankCount 48 `
  -TopK 5 `
  -ApprovalThreshold 0.6
```

Validated result:

- `FP = 2`
- `FN = 5`
- detection score `2623.42`
- search latency:
  - `p50 = 114.8 us`
  - `p95 = 666.2 us`
  - `p99 = 743.8 us`
  - `mean = 270.4 us`

## Full compliant stack

Command:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\benchmark-official-compose.ps1 `
  -RuntimeDataDir runtime-data `
  -BeamLevel1 8 `
  -BeamLevel2 72 `
  -RerankCount 48 `
  -TopK 5 `
  -ApprovalThreshold 0.6 `
  -HttpParserMode Manual `
  -HttpIoQueueCount 0 `
  -HttpInlineScheduling true `
  -HttpNoDelay true `
  -LbCpus 0.15 `
  -ApiCpus 0.425 `
  -LbMemLimit 48m `
  -ApiMemLimit 151m
```

Latest validated result:

- `p99 = 2.08 ms`
- `FP = 2`
- `FN = 5`
- `http_errors = 0`
- final score `5306.37`

Best observed result on the current chosen default:

- `p99 = 2.06 ms`
- `FP = 2`
- `FN = 5`
- `http_errors = 0`
- final score `5309.78`

## Comparison notes

- `256x128` dominated both `512x64` and `1024x32` on scan-count efficiency at the same training budget.
- The best measured full-stack point on the promoted family is `8/72/48`.
- A faster `8/48/48` point on `256x128` reached `p99 = 1.61 ms`, but its detection score was lower and lost the total-score race.
- A more recall-heavy `8/96/48` point reached `FP = 1`, `FN = 3`, but the real stack `p99` collapsed to `3.84 ms`.
- Lowering the LB split from `0.20` to `0.15` improved the promoted point slightly; lowering it to `0.10` caused catastrophic queueing.

## Remaining gap

Current default stack is:

- compliant
- self-contained
- correctness-stable
- benchmarkable with one command

But it is not yet at the target:

- current best observed final score: `5309.78`
- target score: `6000`
- current best observed full-stack p99: `2.06 ms`
- target p99: `<= 0.5 ms`

The next optimization frontier is reducing search-path service time under the real stack, not parser correctness or LB topology.
