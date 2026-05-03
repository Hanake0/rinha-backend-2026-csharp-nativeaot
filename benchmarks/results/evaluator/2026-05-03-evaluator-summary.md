# 2026-05-03 Evaluator Summary

## Scope

This file records the search-only evaluator frontier that chose the current default search configuration for the tracked `runtime-data/` artifact set after q8 leaf-radius pruning was introduced.

## Compared configurations

All runs used:

- parse mode: `ManualParser`
- index kind: `HierarchicalBeamIvf`
- `topK = 5`
- `approvalThreshold = 0.6`
- `useLeafRadiusPruning = true`

| Beam1 | Beam2 | Rerank | FP | FN | Detection | Search mean us | Search p99 us |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 8 | 128 | 48 | 1 | 2 | 2729.07 | 114.4 | 390.8 |
| 8 | 160 | 48 | 1 | 2 | 2729.07 | 116.6 | 405.0 |
| 8 | 96 | 48 | 1 | 3 | 2687.58 | 105.6 | 291.5 |
| 8 | 112 | 48 | 1 | 3 | 2687.58 | 109.7 | 411.4 |
| 8 | 72 | 48 | 2 | 5 | 2623.42 | 97.2 | 436.5 |

## Decision

Chosen default:

- `beamLevel1 = 8`
- `beamLevel2 = 128`
- `rerankCount = 48`
- `useLeafRadiusPruning = true`

Reason:

- it improves detection score over the faster `72/96/112` branches
- it stays below `0.5 ms` on evaluator-side search p99
- `160` does not buy more correctness and is strictly slower

## Service-path note

When the same configuration is validated through `ServiceManual`, the latest measured evaluator-side search latency is:

- `p50 = 104.0 us`
- `p95 = 214.1 us`
- `p99 = 505.1 us`
- `mean = 117.0 us`

Correctness stays at `1 FP / 2 FN`.
