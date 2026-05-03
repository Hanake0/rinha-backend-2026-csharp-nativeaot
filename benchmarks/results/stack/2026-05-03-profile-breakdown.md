# 2026-05-03 Profile Breakdown

## Scope

This file records the constrained full-stack request-stage profile of the promoted `256x128` default submission shape.

## Command

```powershell
powershell -ExecutionPolicy Bypass -File scripts\profile-compose.ps1 `
  -RuntimeDataDir runtime-data-256x128-s524k `
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

## Outputs

- `artifacts/compose-profile/api1-profile.json`
- `artifacts/compose-profile/api2-profile.json`
- `artifacts/compose-profile/memory-samples.csv`

## Request-stage breakdown

API1 sampled `422` requests out of `27,029`:

- `bodyReadUs`: `p50 = 1.944`, `p95 = 2.636`, `p99 = 3.737`
- `parseUs`: `p50 = 2.405`, `p95 = 3.316`, `p99 = 4.178`
- `vectorizeUs`: `p50 = 0.361`, `p95 = 0.521`, `p99 = 0.902`
- `searchUs`: `p50 = 247.582`, `p95 = 1458.977`, `p99 = 1667.811`
- `responseWriteUs`: `p50 = 36.842`, `p95 = 49.455`, `p99 = 67.941`
- `totalUs`: `p50 = 290.156`, `p95 = 1513.403`, `p99 = 1722.137`

API2 sampled `422` requests out of `27,030`:

- `bodyReadUs`: `p50 = 1.924`, `p95 = 2.746`, `p99 = 5.040`
- `parseUs`: `p50 = 2.405`, `p95 = 3.216`, `p99 = 4.258`
- `vectorizeUs`: `p50 = 0.380`, `p95 = 0.681`, `p99 = 1.312`
- `searchUs`: `p50 = 211.470`, `p95 = 1359.863`, `p99 = 1573.081`
- `responseWriteUs`: `p50 = 36.841`, `p95 = 47.392`, `p99 = 54.577`
- `totalUs`: `p50 = 257.439`, `p95 = 1397.507`, `p99 = 1621.976`

## Memory and CPU profile

Representative steady-state samples from `memory-samples.csv`:

- `api1`: max observed `17.20 MiB / 151 MiB`
- `api2`: max observed `16.68 MiB / 151 MiB`
- `lb`: max observed `5.68 MiB / 48 MiB`

## Interpretation

- Body read, JSON parse, and vectorization are already below the noise floor relative to the search path.
- Response write is measurable but still small.
- Search dominates both the median and the tail even after the topology improvement.
- Memory pressure is not the current limiter. There is still room to spend more memory if it buys materially better selectivity or lower rerank cost.
- Response write remains visible at about `37-62 us`, but it is still small relative to search and not the first frontier to attack.

## Immediate implication

The next optimization passes should prioritize:

1. reducing scanned candidates per request
2. improving the top-candidate maintenance cost inside the search kernel
3. testing whether a more selective index topology or search algorithm can keep recall while cutting tail scan cost
4. only then revisiting LB/API transport overhead
