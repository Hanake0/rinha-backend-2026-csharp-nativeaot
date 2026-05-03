# 2026-05-03 Topology Frontier

## Scope

This file records the topology exploration that moved the stack from the promoted `512x64` baseline to the current `256x128` baseline.

Every artifact in this comparison used:

- training sample size `524,288`
- k-means iterations `12`
- `rerankCount = 48`

## Evaluator topology comparison

Traced evaluator sweep:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sweep-evaluator.ps1 `
  -RuntimeDataDirs runtime-data,runtime-data-1024x32-s524k,runtime-data-256x128-s524k `
  -BeamLevel1Values 8,10 `
  -BeamLevel2Values 48,96 `
  -RerankCountValues 48 `
  -ParseMode ManualParser `
  -TraceEvery 64 `
  -OutputCsv artifacts\evaluator-sweeps\topology-trace-comparison.csv
```

Selected outcomes:

| Artifact | Config | FP | FN | Search p99 | Candidate scan p99 |
| --- | --- | ---: | ---: | ---: | ---: |
| `512x64` | `8/48/48` | 6 | 12 | `747.7 us` | `132,477` |
| `512x64` | `10/96/48` | 1 | 3 | `1214.0 us` | `248,651` |
| `1024x32` | `8/48/48` | 17 | 17 | `756.8 us` | `149,378` |
| `1024x32` | `10/96/48` | 2 | 5 | `1407.7 us` | `286,703` |
| `256x128` | `8/48/48` | 9 | 12 | `515.2 us` | `96,824` |
| `256x128` | `8/96/48` | 1 | 3 | `943.9 us` | `193,381` |

Direct result:

- `256x128` was strictly better than the old promoted topology on scan-count efficiency.
- `1024x32` was a dead end on both recall and scan volume.

## `256x128` mid-frontier

Evaluator sweep:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sweep-evaluator.ps1 `
  -RuntimeDataDirs runtime-data-256x128-s524k `
  -BeamLevel1Values 8 `
  -BeamLevel2Values 56,64,72,80,88,96 `
  -RerankCountValues 48 `
  -ParseMode ManualParser `
  -TraceEvery 64 `
  -OutputCsv artifacts\evaluator-sweeps\topology-256x128-midfrontier.csv
```

Selected outcomes:

| Config | FP | FN | Search p99 | Detection |
| --- | ---: | ---: | ---: | ---: |
| `8/56/48` | 7 | 13 | `579.8 us` | `2498.37` |
| `8/64/48` | 5 | 8 | `650.0 us` | `2556.86` |
| `8/72/48` | 2 | 5 | `743.8 us` | `2623.42` |
| `8/80/48` | 1 | 4 | `799.1 us` | `2656.16` |
| `8/96/48` | 1 | 3 | `943.9 us` | `2687.58` |

## Compose validation on `256x128`

All runs used:

- `nginx:1.27-alpine`
- `HttpParserMode = Manual`
- `HttpIoQueueCount = 0`
- `HttpInlineScheduling = true`
- `HttpNoDelay = true`

Selected outcomes:

| Config | CPU split | p99 | FP | FN | Score |
| --- | --- | ---: | ---: | ---: | ---: |
| `8/48/48` | `0.20 / 0.40 / 0.40` | `1.61 ms` | 9 | 12 | `5295.10` |
| `8/64/48` | `0.20 / 0.40 / 0.40` | `1.92 ms` | 5 | 8 | `5274.66` |
| `8/72/48` | `0.20 / 0.40 / 0.40` | `2.06 ms` | 2 | 5 | `5309.23` |
| `8/72/48` | `0.15 / 0.425 / 0.425` | `2.06 ms` | 2 | 5 | `5309.78` |
| `8/80/48` | `0.20 / 0.40 / 0.40` | `2.24 ms` | 1 | 4 | `5305.34` |
| `10/96/48` | `0.20 / 0.40 / 0.40` | `3.84 ms` | 1 | 3 | `5103.25` |
| `8/72/48` | `0.10 / 0.45 / 0.45` | `63.42 ms` | 2 | 5 | `3821.21` |

## Decision

The promoted baseline is:

- artifact: `runtime-data-256x128-s524k`
- search config: `8/72/48`
- CPU split: `lb = 0.15`, `api1 = 0.425`, `api2 = 0.425`

Reason:

- it is the best measured full-stack score so far
- it improves detection materially over the previous promoted default
- it keeps the stack stable under the real constrained benchmark

## Remaining gap

The topology change helped, but it did not remove the core bottleneck:

- service-side tail is still dominated by search
- memory is still not the limiter
- the next meaningful gain must come from cutting search scan cost again, not from parser work or response formatting
