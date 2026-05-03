# 2026-05-03 Stack Summary

## Scope

This file records the validated stack-level results after:

- NativeAOT correctness fix for runtime config binding
- search artifact retraining
- LB CPU split tuning
- socket transport tuning
- HAProxy vs Nginx comparison

## Chosen default

- compose: `docker-compose.yml`
- load balancer: `nginx:1.27-alpine`
- dataset artifact: `runtime-data/`
- search settings:
  - `beamLevel1 = 10`
  - `beamLevel2 = 32`
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

- source build directory promoted into `runtime-data/`: `runtime-data-512x32-s524k`
- hierarchical training:
  - `L1 = 512`
  - `L2 per L1 = 32`
  - training sample size `524,288`
  - k-means iterations `12`

## Full-corpus evaluator

Command shape:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\evaluate-official.ps1 `
  -ParseMode ServiceManual `
  -IndexKind HierarchicalBeamIvf `
  -BeamLevel1 10 `
  -BeamLevel2 32 `
  -RerankCount 48 `
  -TopK 5 `
  -ApprovalThreshold 0.6
```

Validated result:

- `FP = 5`
- `FN = 10`
- detection score `2533.11`

## Full compliant stack

Command:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\benchmark-official-compose.ps1 `
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

Latest validated result:

- `p99 = 2.38 ms`
- `FP = 5`
- `FN = 10`
- `http_errors = 0`
- final score `5157.29`

Best observed result on the current chosen default:

- `p99 = 2.30 ms`
- `FP = 5`
- `FN = 10`
- `http_errors = 0`
- final score `5172.24`

Historical one-off result on a nearby configuration:

- config: `beamLevel1 = 8`, `beamLevel2 = 32`, `rerankCount = 64`
- `p99 = 2.25 ms`
- `FP = 6`
- `FN = 10`
- `http_errors = 0`
- final score `5176.54`

## Comparison notes

- HAProxy with `0.10 CPU` was a false bottleneck and produced catastrophic queueing at `900 req/s`.
- HAProxy stabilized when raised to `0.20 CPU`, but Nginx remained slightly faster on the same resource envelope.
- The confirmed best repeated full-stack tradeoff on this artifact set was `beamLevel1 = 10`, `beamLevel2 = 32`, `rerankCount = 48`.
- A historical one-off `8/32/64` run scored slightly higher, but confirmation reruns favored `10/32/48` as the more defensible default.
- Narrowing to `beamLevel2 = 24` improved latency, but the detection loss was too large and reduced total score.

## Remaining gap

Current default stack is:

- compliant
- self-contained
- correctness-stable
- benchmarkable with one command

But it is not yet at the target:

- current best observed final score: `5172.24`
- target score: `6000`
- current best observed full-stack p99: `2.30 ms`
- target p99: `<= 0.5 ms`

The next optimization frontier is reducing search-path service time under the real stack, not parser correctness or LB topology.
