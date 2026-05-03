# 2026-05-03 Profile Breakdown

## Scope

This file records the constrained full-stack request-stage profile captured after q8 leaf-radius pruning made the wider-leaf frontier viable.

The sampled profile below used the near-best `8/96/48` point on the pruning-enabled artifact family because it was stable and representative of the new search-path shape.

## Command

```powershell
powershell -ExecutionPolicy Bypass -File scripts\profile-compose.ps1 `
  -RuntimeDataDir runtime-data-256x128-radii-s524k `
  -BeamLevel1 8 `
  -BeamLevel2 96 `
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

## Outputs

- `artifacts/compose-profile/api1-profile.json`
- `artifacts/compose-profile/api2-profile.json`
- `artifacts/compose-profile/memory-samples.csv`

## Request-stage breakdown

API1 sampled `422` requests out of `27,029`:

- `bodyReadUs`: `p50 = 1.794`, `p95 = 3.718`, `p99 = 7.284`
- `parseUs`: `p50 = 2.294`, `p95 = 3.715`, `p99 = 5.120`
- `vectorizeUs`: `p50 = 0.361`, `p95 = 0.741`, `p99 = 1.342`
- `searchUs`: `p50 = 182.303`, `p95 = 503.231`, `p99 = 621.202`
- `responseWriteUs`: `p50 = 36.160`, `p95 = 46.451`, `p99 = 52.783`
- `totalUs`: `p50 = 225.705`, `p95 = 558.397`, `p99 = 679.125`

API2 sampled `422` requests out of `27,030`:

- `bodyReadUs`: `p50 = 1.784`, `p95 = 3.522`, `p99 = 6.272`
- `parseUs`: `p50 = 2.314`, `p95 = 3.684`, `p99 = 4.950`
- `vectorizeUs`: `p50 = 0.371`, `p95 = 0.852`, `p99 = 1.423`
- `searchUs`: `p50 = 190.178`, `p95 = 525.153`, `p99 = 601.502`
- `responseWriteUs`: `p50 = 36.069`, `p95 = 47.944`, `p99 = 61.630`
- `totalUs`: `p50 = 235.023`, `p95 = 575.195`, `p99 = 641.539`

## Memory and CPU profile

Representative steady-state samples from `memory-samples.csv`:

- `api1`: max observed `17.20 MiB / 151 MiB`
- `api2`: max observed `16.68 MiB / 151 MiB`
- `lb`: max observed `5.68 MiB / 48 MiB`

## Interpretation

- body read, JSON parse, and vectorization are already close to noise relative to the search path
- response write is visible but still small
- search remains the dominant service-side cost
- service-side p99 is now about `0.64-0.68 ms`
- the external full-stack p99 at about `1.10 ms` means LB/network-visible overhead is now material
- memory pressure is still not the current limiter

## Immediate implication

The next optimization passes should prioritize:

1. reducing the remaining search tail without giving back correctness
2. measuring LB/runtime overhead directly against the current best search point
3. only spending more memory if it buys a measurable gain under the actual compose limits
