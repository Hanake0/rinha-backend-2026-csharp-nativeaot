# 2026-05-03 Trace Frontier

## Scope

This file records the first traced evaluator sweep on the promoted `512x64` artifact family. The goal was to measure why the current frontier moves the way it does, not only which point scores better.

## Command

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\sweep-evaluator.ps1 `
  -RuntimeDataDirs runtime-data `
  -BeamLevel1Values 8,10 `
  -BeamLevel2Values 48,96 `
  -RerankCountValues 48 `
  -ParseMode ManualParser `
  -TraceEvery 64 `
  -OutputCsv artifacts\evaluator-sweeps\trace-512x64-frontier.csv
```

## Selected results

| Config | FP | FN | Search p99 | Candidate scan p50 | Candidate scan p95 | Candidate scan p99 | Max leaf size p99 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `8/48/48` | 6 | 12 | `666.6 us` | `12,105` | `118,853` | `132,477` | `4,258` |
| `10/48/48` | 6 | 13 | `696.8 us` | `12,105` | `118,853` | `132,477` | `4,258` |
| `8/96/48` | 2 | 3 | `1307.7 us` | `24,584` | `230,218` | `248,651` | `4,258` |
| `10/96/48` | 1 | 3 | `1276.3 us` | `24,584` | `230,218` | `248,651` | `4,258` |

## Direct findings

- `beamLevel1 = 8` and `beamLevel1 = 10` are effectively equivalent on scan cost for this artifact family at the tested `beamLevel2` values.
- Doubling `beamLevel2` from `48` to `96` roughly doubles candidate scan volume and pushes evaluator search `p99` from about `0.67 ms` to about `1.28 ms`.
- `rerankCount = 48` is not the limiting factor on this frontier; candidate scan count is.
- `SecondaryCandidateScanCount` stayed at zero across this traced sample, which means the last-transaction fallback path is not the current tail source.

## Implication

The next meaningful optimization is not another small arithmetic tweak. It is a structural reduction in scanned vectors per request, most likely through:

1. tighter leaf selectivity from a different topology or index family
2. leaf-level pruning with conservative lower bounds
3. an alternate ANN structure that gives better recall per scanned vector
