# 2026-05-03 Last-Transaction Partitioning

## Goal

Test whether domain-aware partitioning on the `last_transaction` null sentinel can cut candidate scan cost and improve the real constrained stack.

## Implementation

- builder option: `UseLastTransactionPartitioning`
- runtime option: `UseLastTransactionPartitionPruning`
- artifact change:
  - reorder vectors inside each IVF leaf so `last_transaction = null` vectors come first
  - emit `leaf.without-history.counts.bin`
- search change:
  - scan only the query-matching history partition first
  - skip the opposite partition when the provisional exact top-`k` worst distance is below the squared L2 lower bound of `2.0`

## Evaluator comparison

Configuration held constant across all runs:

- runtime: `beamLevel1 = 10`, `beamLevel2 = 32`, `rerankCount = 48`, `topK = 5`
- parse mode: `ManualParser`
- official corpus: `54100` requests

### Baseline artifact

- runtime data: `runtime-data/`
- pruning flag: `true` (no metadata present, so no effect)
- detection: `FP = 5`, `FN = 10`, score `2533.11`
- search latency:
  - `p50 = 151.7 us`
  - `p95 = 819.3 us`
  - `p99 = 1000.8 us`
- trace:
  - `CandidateScanCount mean = 57572`
  - `SecondaryCandidateScanCount mean = 0`

### History-split artifact, pruning disabled

- runtime data: `runtime-data-512x32-s524k-historysplit/`
- pruning flag: `false`
- detection: `FP = 5`, `FN = 10`, score `2533.11`
- search latency:
  - `p50 = 147.3 us`
  - `p95 = 802.3 us`
  - `p99 = 915.1 us`
- trace:
  - `CandidateScanCount mean = 57572`
  - `SecondaryCandidateScanCount mean = 0`

### History-split artifact, pruning enabled

- runtime data: `runtime-data-512x32-s524k-historysplit/`
- pruning flag: `true`
- detection: `FP = 5`, `FN = 10`, score `2533.11`
- search latency:
  - `p50 = 144.8 us`
  - `p95 = 785.7 us`
  - `p99 = 899.7 us`
- trace:
  - `CandidateScanCount mean = 57572`
  - `SecondaryCandidateScanCount mean = 0`

## Full stack comparison

Configuration held constant across both stack runs:

- compose: `docker-compose.yml`
- runtime data: `runtime-data-512x32-s524k-historysplit/`
- `beamLevel1 = 10`, `beamLevel2 = 32`, `rerankCount = 48`, `topK = 5`
- `HttpParserMode = Manual`
- `Runtime:Http:IoQueueCount = 0`
- `Runtime:Http:UnsafePreferInlineScheduling = true`
- `Runtime:Http:NoDelay = true`
- resources:
  - `lb = 0.20 CPU / 48 MB`
  - `api1 = 0.40 CPU / 151 MB`
  - `api2 = 0.40 CPU / 151 MB`

### History-split artifact, pruning disabled

- `p99 = 2.35 ms`
- final score `5162.57`
- detection remained `FP = 5`, `FN = 10`

### History-split artifact, pruning enabled

- `p99 = 2.32 ms`
- final score `5167.98`
- detection remained `FP = 5`, `FN = 10`

## Decision

- keep the change as a validated optimization branch
- do not promote it as the repo default yet

Reason:

- it is a real improvement over the same rebuilt artifact without pruning
- it does not beat the repo's best observed stack result (`p99 = 2.30 ms`, final `5172.24`)
- the expected scan-count collapse did not materialize because the current centroiding already separates the history-null sentinel strongly enough that opposite-partition scans were effectively never triggered in the evaluator trace

## Implication for the next branch

The next likely wins are lower-level search kernel changes:

1. cheaper fixed-capacity top-candidate maintenance while scanning leaves
2. faster rerank distance computation, ideally with explicit SIMD on the f16 path
3. transport experiments only after search-kernel work stops paying
