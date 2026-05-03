# 2026-05-03 Stack Summary

## Scope

This file records the validated stack-level results after:

- search artifact retraining
- deeper evaluator frontier sweeps
- constrained compose validation of the new frontier
- request-stage profiling on the promoted baseline

## Chosen default

- compose: `docker-compose.yml`
- load balancer: `nginx:1.27-alpine`
- dataset artifact: `runtime-data/`
- search settings:
  - `beamLevel1 = 8`
  - `beamLevel2 = 48`
  - `rerankCount = 48`
  - `topK = 5`
  - `approvalThreshold = 0.6`
- transport settings:
  - `Runtime:Http:IoQueueCount = 0`
  - `Runtime:Http:UnsafePreferInlineScheduling = true`
  - `Runtime:Http:NoDelay = true`
- resource split:
  - `lb = 0.20 CPU / 48 MB`
  - `api1 = 0.40 CPU / 151 MB`
  - `api2 = 0.40 CPU / 151 MB`

## Artifact provenance

- source build directory promoted into `runtime-data/`: `runtime-data-512x64-s524k`
- hierarchical training:
  - `L1 = 512`
  - `L2 per L1 = 64`
  - training sample size `524,288`
  - k-means iterations `12`

## Full-corpus evaluator

Command shape:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\evaluate-official.ps1 `
  -ParseMode ServiceManual `
  -IndexKind HierarchicalBeamIvf `
  -BeamLevel1 8 `
  -BeamLevel2 48 `
  -RerankCount 48 `
  -TopK 5 `
  -ApprovalThreshold 0.6
```

Validated result:

- `FP = 6`
- `FN = 12`
- detection score `2509.96`
- search latency:
  - `p50 = 114.2 us`
  - `p95 = 590.7 us`
  - `p99 = 713.1 us`
  - `mean = 240.4 us`

## Full compliant stack

Command:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\benchmark-official-compose.ps1 `
  -RuntimeDataDir runtime-data `
  -BeamLevel1 8 `
  -BeamLevel2 48 `
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

Latest validated result:

- `p99 = 1.89 ms`
- `FP = 6`
- `FN = 12`
- `http_errors = 0`
- final score `5233.41`

Best observed result on the current chosen default:

- `p99 = 1.89 ms`
- `FP = 6`
- `FN = 12`
- `http_errors = 0`
- final score `5234.56`

## Comparison notes

- The older `512x32` artifact family could trade a little detection for speed, but it flattened out around the low `5.17k` score range.
- The promoted `512x64` artifact moved the frontier forward enough to justify a new default.
- The best current full-stack point on the promoted family is `8/48/48`.
- A more recall-heavy `10/96/48` evaluator configuration reached `FP = 1`, `FN = 3`, but its evaluator `p99` was about `1.29 ms`, so it is not yet compatible with the latency target.
- The request-stage profile confirms the same conclusion as the evaluator: search dominates the tail, while parse, vectorize, and write stay comparatively small.

## Remaining gap

Current default stack is:

- compliant
- self-contained
- correctness-stable
- benchmarkable with one command

But it is not yet at the target:

- current best observed final score: `5234.56`
- target score: `6000`
- current best observed full-stack p99: `1.89 ms`
- target p99: `<= 0.5 ms`

The next optimization frontier is reducing search-path service time under the real stack, not parser correctness or LB topology.
