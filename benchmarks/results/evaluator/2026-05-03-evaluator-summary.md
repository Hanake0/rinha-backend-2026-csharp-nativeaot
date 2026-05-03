# 2026-05-03 Evaluator Summary

## Scope

This file records the full official-corpus evaluator comparison used to choose the current default search configuration for the tracked `runtime-data/` artifact set.

## Compared configurations

All runs used:

- parse mode: `ServiceManual`
- index kind: `HierarchicalBeamIvf`
- `topK = 5`
- `approvalThreshold = 0.6`

| Beam1 | Beam2 | Rerank | FP | FN | Detection | Search mean us | Search p99 us |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | 32 | 48 | 5 | 10 | 2533.11 | 305.2 | 919.1 |
| 8 | 32 | 64 | 6 | 10 | 2529.54 | 329.8 | 1324.4 |
| 8 | 32 | 48 | 6 | 10 | 2529.54 | 327.1 | 1340.8 |
| 8 | 24 | 48 | 12 | 17 | 2392.00 | 241.5 | 931.9 |
| 10 | 24 | 48 | 12 | 18 | 2365.83 | 244.0 | 950.6 |

## Decision

Chosen default:

- `beamLevel1 = 10`
- `beamLevel2 = 32`
- `rerankCount = 48`

Reason:

- it improved detection score over the previous `8/32/64` default;
- it reduced evaluator search mean and p99 relative to `8/32/64`;
- the faster `24`-leaf branch improved latency, but lost too much detection score to win the end-to-end stack benchmark.
