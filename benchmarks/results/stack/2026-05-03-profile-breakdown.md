# 2026-05-03 Profile Breakdown

## Scope

This file records the first constrained full-stack request-stage profile of the current default submission shape.

## Command

```powershell
powershell -ExecutionPolicy Bypass -File scripts\profile-compose.ps1 `
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

## Outputs

- `artifacts/compose-profile/api1-profile.json`
- `artifacts/compose-profile/api2-profile.json`
- `artifacts/compose-profile/memory-samples.csv`

## Request-stage breakdown

API1 sampled `422` requests out of `27,029`:

- `bodyReadUs`: `p50 = 2.114`, `p95 = 3.887`, `p99 = 6.041`
- `parseUs`: `p50 = 2.494`, `p95 = 3.487`, `p99 = 4.709`
- `vectorizeUs`: `p50 = 0.411`, `p95 = 0.962`, `p99 = 1.402`
- `searchUs`: `p50 = 360.255`, `p95 = 1627.376`, `p99 = 1822.079`
- `responseWriteUs`: `p50 = 38.684`, `p95 = 54.435`, `p99 = 70.576`
- `totalUs`: `p50 = 408.628`, `p95 = 1671.050`, `p99 = 1865.041`

API2 sampled `422` requests out of `27,030`:

- `bodyReadUs`: `p50 = 2.204`, `p95 = 4.539`, `p99 = 16.030`
- `parseUs`: `p50 = 2.625`, `p95 = 3.797`, `p99 = 5.380`
- `vectorizeUs`: `p50 = 0.411`, `p95 = 1.052`, `p99 = 1.673`
- `searchUs`: `p50 = 237.457`, `p95 = 1663.871`, `p99 = 1869.753`
- `responseWriteUs`: `p50 = 38.193`, `p95 = 53.743`, `p99 = 67.691`
- `totalUs`: `p50 = 281.062`, `p95 = 1716.886`, `p99 = 1925.380`

## Memory and CPU profile

Representative steady-state samples from `memory-samples.csv`:

- `api1`: about `16.4 MiB / 151 MiB`, `31-32%` CPU
- `api2`: about `16.5 MiB / 151 MiB`, `29-32%` CPU
- `lb`: about `9.3 MiB / 48 MiB`, `10-13%` CPU

## Interpretation

- Body read, JSON parse, and vectorization are already below the noise floor relative to the search path.
- Response write is measurable but still small.
- Search dominates both the median and the tail.
- Memory pressure is not the current limiter. There is still room to spend more memory if it buys materially better selectivity or lower rerank cost.

## Immediate implication

The next optimization passes should prioritize:

1. reducing scanned candidates per request
2. improving the top-candidate maintenance cost inside the search kernel
3. only then revisiting LB/API transport overhead
