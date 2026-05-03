# 2026-05-03 Memory-Mode Promotion Summary

## Scope

This pass tested whether we could use more of the API memory budget by promoting index artifacts from shared read-only memory maps into process memory.

Modes tested:

- `MemoryMapped`
- `PromoteMetadata`
- `PromoteMetadataAndQuantizedVectors`

The goal was to reduce search tail latency without changing correctness.

## Why this was worth testing

The accepted exact baseline only showed about `17 MiB` of API memory under load, while each API had a `151 MiB` limit. The runtime also already used file-backed `mmap`, so the only sensible memory experiment was selective promotion of hot structures:

- label bitset
- stable-order ids
- centroids
- posting offsets and posting ids
- optionally the q8 scan corpus

## Host-side micro-benchmarks

Relevant benchmark excerpts:

### Exact flat top-5

- `MemoryMapped`: `23.145 us`
- `PromoteMetadata`: `22.140 us`
- `PromoteMetadataAndQuantizedVectors`: `22.891 us`

### Hierarchical approximate top-5

- `MemoryMapped`: `1.615 us`
- `PromoteMetadata`: `1.612 us`
- `PromoteMetadataAndQuantizedVectors`: `1.622 us`

### Hierarchical exact top-5

- `MemoryMapped`: `23.435 us`
- `PromoteMetadata`: `22.809 us`
- `PromoteMetadataAndQuantizedVectors`: `22.512 us`

Interpretation:

- host-native micro-benchmarks suggested a small upside for `PromoteMetadata`
- the q8 promotion path was neutral to slightly worse on the approximate kernel
- none of these wins were large enough to trust without compose validation

## Full-corpus evaluator

Configuration:

- runtime-data: `runtime-data-256x128-radii-f32-stable-s524k`
- parse mode: `ServiceManual`
- frontier: `8 / 128 / 48`
- `topK = 5`
- `approvalThreshold = 0.6`
- `useLeafRadiusPruning = true`
- `useLastTransactionPartitionPruning = false`

Results:

| Mode | FP | FN | Search p99 | Search mean | Throughput |
| --- | ---: | ---: | ---: | ---: | ---: |
| `MemoryMapped` | 0 | 0 | `563.7 us` | `117.3 us` | `8434.74 / s` |
| `PromoteMetadata` | 0 | 0 | `554.5 us` | `116.5 us` | `8488.99 / s` |
| `PromoteMetadataAndQuantizedVectors` | 0 | 0 | `609.1 us` | `119.3 us` | `8294.58 / s` |

Interpretation:

- correctness stayed exact
- `PromoteMetadata` looked mildly positive in evaluator space
- promoting the q8 scan corpus already looked worse before compose

## Constrained compose

Configuration:

- same exact stable dataset and frontier
- `lb = 0.15 CPU / 48 MiB`
- `api = 0.425 CPU / 151 MiB`
- `Runtime:Http:UnsafePreferInlineScheduling = true`
- manual parser

Results:

| Mode | p99 | Final score | FP | FN | HTTP errors |
| --- | ---: | ---: | ---: | ---: | ---: |
| `MemoryMapped` | `1.61 ms` | `5793.35` | 0 | 0 | 0 |
| `PromoteMetadata` | `2.04 ms` | `5689.64` | 0 | 0 | 0 |
| `PromoteMetadataAndQuantizedVectors` | `3.22 ms` | `5492.60` | 0 | 0 | 0 |

Interpretation:

- the micro win did not survive the real stack
- the metadata-only mode regressed sharply
- promoting q8 vectors regressed even harder

## Conclusion

This branch is rejected.

The runtime is already using shared file-backed `mmap`, and promoting artifacts into each API process made the real stack slower under the constrained envelope. The next viable memory architecture is not “copy more into both APIs”; it is a topology that keeps one shared in-memory index in a separate process and makes the APIs thin.
