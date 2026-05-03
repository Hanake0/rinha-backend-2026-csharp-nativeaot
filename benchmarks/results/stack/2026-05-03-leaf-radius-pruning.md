# 2026-05-03 Leaf Radius Pruning

## Scope

This file records the first search-structure win after the `256x128` topology promotion: optional q8 leaf-radius pruning.

The goal was simple:

- cut median and tail candidate scans without changing the detected top hits
- keep the code path configurable
- validate on both the evaluator and the constrained compose stack

## Implementation summary

- offline index builder now emits `leaf.radius.q8.f32.bin`
- runtime artifact loader maps the optional radius file when present
- search runtime adds `UseLeafRadiusPruning`
- hierarchical search prunes a leaf only after the candidate reservoir is full and the leaf centroid lower bound cannot beat the current reservoir threshold

## Artifact

- source build directory: `runtime-data-256x128-radii-s524k`
- promoted runtime directory: `runtime-data/`

## Evaluator comparison

Compared on the same artifact family with `beamLevel1 = 8`, `beamLevel2 = 72`, `rerankCount = 48`:

| Pruning | FP | FN | Detection | Search mean us | Search p99 us | Mean pruned leaves | Mean pruned candidates |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| off | 2 | 5 | 2623.42 | 270.4 | 751.8 | 0 | 0 |
| on | 2 | 5 | 2623.42 | 97.2 | 436.5 | 31 | 36,364 |

## Compose frontier on pruning-enabled artifact

All runs used:

- `beamLevel1 = 8`
- `rerankCount = 48`
- `useLeafRadiusPruning = true`
- `useLastTransactionPartitionPruning = true`
- `HttpParserMode = Manual`
- `HttpIoQueueCount = 0`
- `HttpInlineScheduling = true`
- `HttpNoDelay = true`
- `lb = 0.15 CPU / 48 MB`
- `api = 0.425 CPU / 151 MB`

| Beam2 | p99 ms | FP | FN | Final |
| ---: | ---: | ---: | ---: | ---: |
| 72 | 1.14 | 2 | 5 | 5567.40 |
| 80 | 1.09 | 1 | 4 | 5618.90 |
| 96 | 1.10 | 1 | 3 | 5645.94 |
| 128 | 1.10 | 1 | 2 | 5688.47 |

## Current decision

Promote:

- artifact family `runtime-data-256x128-radii-s524k`
- `beamLevel1 = 8`
- `beamLevel2 = 128`
- `rerankCount = 48`
- `useLeafRadiusPruning = true`

Reason:

- same or better stack p99 than the older `8/72/48` point
- materially better correctness
- evaluator-side p99 is now below `0.5 ms`
- service-side p99 is now around `0.64-0.68 ms`, which shifts the next frontier toward LB/runtime overhead and the last `1 FP / 2 FN`
