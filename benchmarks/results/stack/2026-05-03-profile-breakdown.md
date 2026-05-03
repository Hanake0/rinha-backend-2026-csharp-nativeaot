# 2026-05-03 Profile Breakdown

## Scope

This file records the constrained full-stack request-stage profile of the promoted `512x64` default submission shape.

## Command

```powershell
powershell -ExecutionPolicy Bypass -File scripts\profile-compose.ps1 `
  -RuntimeDataDir runtime-data-512x64-s524k `
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

## Outputs

- `artifacts/compose-profile/api1-profile.json`
- `artifacts/compose-profile/api2-profile.json`
- `artifacts/compose-profile/memory-samples.csv`

## Request-stage breakdown

API1 sampled `422` requests out of `27,029`:

- `bodyReadUs`: `p50 = 1.824`, `p95 = 3.256`, `p99 = 6.012`
- `parseUs`: `p50 = 2.385`, `p95 = 3.397`, `p99 = 4.910`
- `vectorizeUs`: `p50 = 0.351`, `p95 = 0.511`, `p99 = 1.052`
- `searchUs`: `p50 = 280.223`, `p95 = 1268.185`, `p99 = 1457.232`
- `responseWriteUs`: `p50 = 36.992`, `p95 = 49.055`, `p99 = 60.868`
- `totalUs`: `p50 = 327.363`, `p95 = 1315.487`, `p99 = 1507.600`

API2 sampled `422` requests out of `27,030`:

- `bodyReadUs`: `p50 = 1.793`, `p95 = 3.166`, `p99 = 7.705`
- `parseUs`: `p50 = 2.375`, `p95 = 3.407`, `p99 = 5.080`
- `vectorizeUs`: `p50 = 0.371`, `p95 = 0.712`, `p99 = 1.422`
- `searchUs`: `p50 = 204.696`, `p95 = 1232.808`, `p99 = 1526.606`
- `responseWriteUs`: `p50 = 37.142`, `p95 = 48.804`, `p99 = 62.464`
- `totalUs`: `p50 = 251.547`, `p95 = 1274.057`, `p99 = 1570.781`

## Memory and CPU profile

Representative steady-state samples from `memory-samples.csv`:

- `api1`: max observed `16.77 MiB / 151 MiB`
- `api2`: max observed `16.89 MiB / 151 MiB`
- `lb`: max observed `5.38 MiB / 48 MiB`

## Interpretation

- Body read, JSON parse, and vectorization are already below the noise floor relative to the search path.
- Response write is measurable but still small.
- Search dominates both the median and the tail.
- Memory pressure is not the current limiter. There is still room to spend more memory if it buys materially better selectivity or lower rerank cost.
- Response write remains visible at about `37-62 us`, but it is still small relative to search and not the first frontier to attack.

## Immediate implication

The next optimization passes should prioritize:

1. reducing scanned candidates per request
2. improving the top-candidate maintenance cost inside the search kernel
3. testing whether a more selective index topology or search algorithm can keep recall while cutting tail scan cost
4. only then revisiting LB/API transport overhead
