# 2026-05-03 Candidate Reservoir

## Goal

Reduce leaf-scan maintenance overhead while preserving the exact same candidate set semantics for rerank.

## Hypothesis

The search path does not need the top `rerankCount` candidate ids kept in sorted order while scanning IVF postings. It only needs a fixed-capacity set of the best distances, because exact rerank re-sorts the final hits anyway.

That means the current sorted insertion path is paying avoidable work on every accepted candidate.

## Microbenchmark

Benchmark: `CandidateSelectionBenchmarks`

Compared strategies:

- sorted insertion into a fixed-capacity ascending buffer
- unsorted fixed-capacity reservoir with tracked current max
- fixed-capacity max-heap

Results:

- `CandidateCount = 50,000`
  - sorted insertion: `56.33 us`
  - unsorted max-scan reservoir: `48.28 us`
  - max-heap reservoir: `49.88 us`
- `CandidateCount = 160,000`
  - sorted insertion: `163.63 us`
  - unsorted max-scan reservoir: `140.40 us`
  - max-heap reservoir: `154.01 us`

Decision from the kernel benchmark:

- the unsorted reservoir is the best option
- it is about `14%` faster than the current sorted insertion path
- it is simpler than the heap variant

## Production change

Search-path change in `HierarchicalBeamSearchEngine`:

- keep parent and leaf beam selection sorted, unchanged
- replace candidate maintenance during posting scans with an unsorted fixed-capacity reservoir
- track:
  - `currentMaxIndex`
  - `currentMaxDistance`
- when full:
  - reject candidates `>= currentMaxDistance`
  - otherwise overwrite the current max slot and rescan the small reservoir to find the new max

This keeps the exact same candidate acceptance semantics as the old sorted buffer, without the per-insert shift cost.

## Evaluator comparison

Configuration held constant:

- runtime data: `runtime-data/`
- `beamLevel1 = 10`
- `beamLevel2 = 32`
- `rerankCount = 48`
- `topK = 5`
- `ApprovalThreshold = 0.6`
- `ParseMode = ManualParser`
- `UseLastTransactionPartitionPruning = true`

Detection:

- before: `FP = 5`, `FN = 10`
- after: `FP = 5`, `FN = 10`

Search latency:

- before:
  - `p50 = 151.7 us`
  - `p95 = 819.3 us`
  - `p99 = 1000.8 us`
  - `mean = 321.0 us`
- after:
  - `p50 = 128.7 us`
  - `p95 = 763.2 us`
  - `p99 = 885.6 us`
  - `mean = 298.1 us`

Trace:

- `CandidateScanCount mean = 57572`
- `SelectedLeafCount mean = 32`
- `CandidateRerankCount mean = 48`
- `SecondaryCandidateScanCount mean = 0`

Interpretation:

- the win came from cheaper reservoir maintenance
- scan count stayed flat, so this branch did not improve selectivity

## Full stack comparison

Configuration held constant:

- compose: `docker-compose.yml`
- runtime data: `runtime-data/`
- `beamLevel1 = 10`
- `beamLevel2 = 32`
- `rerankCount = 48`
- `topK = 5`
- `ApprovalThreshold = 0.6`
- `HttpParserMode = Manual`
- `Runtime:Http:IoQueueCount = 0`
- `Runtime:Http:UnsafePreferInlineScheduling = true`
- `Runtime:Http:NoDelay = true`
- resources:
  - `lb = 0.20 CPU / 48 MB`
  - `api1 = 0.40 CPU / 151 MB`
  - `api2 = 0.40 CPU / 151 MB`

Result:

- `FP = 5`
- `FN = 10`
- `http_errors = 0`
- `p99 = 2.31 ms`
- final score `5169.92`

Container memory at the end of the run:

- `lb ~= 5.08 MiB / 48 MiB`
- `api1 ~= 20.85 MiB / 151 MiB`
- `api2 ~= 20.52 MiB / 151 MiB`

## Decision

- promote this optimization into the main branch

Reason:

- no correctness regression
- material evaluator latency improvement
- real full-stack improvement under the constrained compose benchmark
- simple implementation with low maintenance cost

## Next branch

The next likely win is still inside search:

1. faster exact rerank distance on the f16 path
2. only after that, re-open transport and LB overhead experiments
