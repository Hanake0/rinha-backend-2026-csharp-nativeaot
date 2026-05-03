# 2026-05-03 Round4 Stable Summary

## Scope

This file records the first exact `0 / 0` baseline on the corrected stable artifact family and the follow-up validation passes that determined what to keep and what to discard.

Accepted work in this pass:

- stable original-order tie breaking
- full-precision f32 rerank vectors
- official generator-compatible round4 query vectorization
- constrained transport comparison harness

Rejected work in this pass:

- AVX-specific f32 rerank fast path in the service runtime

## Validated artifact family

- runtime-data root: `runtime-data-256x128-radii-f32-stable-s524k`
- topology:
  - `L1 = 256`
  - `L2 per L1 = 128`
- training sample size: `524,288`
- k-means iterations: `12`
- rerank payload:
  - `vectors.f16.bin`
  - `vectors.f32.bin`
  - `vectors.original.ids.bin`

## Full-corpus evaluator

Command:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\evaluate-official.ps1 `
  -RuntimeDataRoot .\runtime-data-256x128-radii-f32-stable-s524k `
  -ParseMode ServiceManual `
  -IndexKind HierarchicalBeamIvf `
  -BeamLevel1 8 `
  -BeamLevel2 128 `
  -RerankCount 48 `
  -TopK 5 `
  -ApprovalThreshold 0.6 `
  -UseLeafRadiusPruning 1 `
  -UseLastTransactionPartitionPruning 0
```

Validated result:

- `FP = 0`
- `FN = 0`
- detection score `3000`
- search latency:
  - `p50 = 104.4 us`
  - `p95 = 211.4 us`
  - `p99 = 510.6 us`
  - `mean = 117.4 us`

Partition-pruning check:

- same artifact, same frontier
- `UseLastTransactionPartitionPruning = true`
- still `FP = 0`, `FN = 0`
- search `p99 = 524.5 us`
- not promoted

## Full compliant stack

Command family:

```powershell
$env:DOTNET_PROCESSOR_COUNT='1'
$env:DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS='1'
$env:DOTNET_SYSTEM_NET_SOCKETS_THREAD_COUNT='1'

.\scripts\benchmark-official-compose.ps1 `
  -RuntimeDataDir .\runtime-data-256x128-radii-f32-stable-s524k `
  -BeamLevel1 8 `
  -BeamLevel2 128 `
  -RerankCount 48 `
  -TopK 5 `
  -ApprovalThreshold 0.6 `
  -UseLeafRadiusPruning 1 `
  -UseLastTransactionPartitionPruning 0 `
  -HttpInlineScheduling true `
  -HttpParserMode Manual `
  -LbCpus 0.15 `
  -ApiCpus 0.425 `
  -LbMemLimit 48m `
  -ApiMemLimit 151m
```

Latest reproduced clean baseline:

- `p99 = 1.38 ms`
- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- final score `5860.13`

Best observed on the corrected stable family:

- `p99 = 1.35 ms`
- detection `0 / 0`
- final score `5869.95`

Partition-pruning stack check:

- exact on detection
- `p99 = 1.49 ms`
- final score `5827.65`
- not promoted

AVX branch stack check:

- exact on detection
- `p99 = 1.47 ms`
- final score `5832.55`
- rejected

## Transport findings

- A direct multi-target API benchmark harness now exists for transport comparison.
- The first direct round-robin probe was not apples-to-apples with the LB path because it changed connection reuse behavior across multiple upstream URLs.
- Current conclusion:
  - do not blame nginx yet
  - keep using the constrained official stack as the truth source
  - use the transport harness only for targeted isolation experiments

## Current conclusion

The submission candidate is now exact on the official evaluator path and within striking distance of `6000`, but the remaining gap is still on the latency side.

The next justified optimization branch is:

1. reduce q8 candidate scan cost with exact-safe pruning or thresholded early-exit
2. re-measure service-side profile under the same `0 / 0` configuration
3. only escalate to deeper transport/runtime surgery if search-side gains flatten
