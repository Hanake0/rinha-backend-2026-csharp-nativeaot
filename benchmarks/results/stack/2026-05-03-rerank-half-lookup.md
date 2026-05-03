# 2026-05-03 Rerank Half Lookup

## Goal

Reduce exact rerank cost on the f16 path without changing search semantics.

## Hypothesis

The current rerank path pays too much for repeated `Half -> float` conversion inside the tight exact distance loop.

Since the stored rerank vectors are raw IEEE 754 half bits and the padded vector width is always `16`, we can:

- replace per-lane `Half` casts with a `ushort -> float` lookup table
- add a fixed-width `16`-lane fast path

Memory cost of the lookup table:

- `65,536 * 4 bytes = 256 KiB` per process

That is negligible inside the current memory envelope.

## Microbenchmark

Benchmark: `RerankDistanceBenchmarks`

Strategies compared:

- current scalar half-cast loop
- scalar lookup loop
- unrolled 16-lane lookup loop
- production distance path after the code change

Results:

- current scalar half-cast loop: `19.182 ns`
- scalar lookup loop: `6.355 ns`
- unrolled 16-lane lookup loop: `5.303 ns`
- production distance path: `4.948 ns`

Decision from the kernel benchmark:

- the lookup-table approach is the clear winner
- the production path is about `74%` faster than the original half-cast loop

## Production change

Change in `DistanceComputations.SquaredL2F16`:

- reinterpret the encoded vector as `ushort` half bits
- use a static `ushort -> float` lookup table
- use a fixed `16`-lane unrolled path for the common case
- keep a general scalar lookup loop as the fallback for any other width

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
  - `p50 = 128.7 us`
  - `p95 = 763.2 us`
  - `p99 = 885.6 us`
  - `mean = 298.1 us`
- after:
  - `p50 = 131.9 us`
  - `p95 = 764.4 us`
  - `p99 = 981.8 us`
  - `mean = 297.5 us`

Interpretation:

- mean stayed slightly better
- p99 moved around enough that the evaluator gain is not decisive on its own
- correctness stayed identical

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
- final score `5169.15`

This is effectively flat against the previous current-default stack result.

## Real-stack profile after the change

API1:

- `bodyReadUs p50/p99 = 1.83 / 15.35`
- `parseUs p50/p99 = 2.38 / 3.60`
- `vectorizeUs p50/p99 = 0.36 / 1.22`
- `searchUs p50/p99 = 345.77 / 1804.09`
- `responseWriteUs p50/p99 = 36.66 / 55.34`
- `totalUs p50/p99 = 397.20 / 1833.73`

API2:

- `bodyReadUs p50/p99 = 1.78 / 3.57`
- `parseUs p50/p99 = 2.36 / 4.23`
- `vectorizeUs p50/p99 = 0.36 / 1.25`
- `searchUs p50/p99 = 232.41 / 1823.24`
- `responseWriteUs p50/p99 = 36.66 / 59.90`
- `totalUs p50/p99 = 273.34 / 1864.78`

Memory under load:

- APIs climbed gradually and stayed well below limit, around the mid-teen MiB range
- LB remained around `4-5 MiB`

Interpretation:

- rerank got cheaper
- the service-side tail still belongs overwhelmingly to search selectivity and leaf scan cost
- transport and write overhead did not become the primary blocker

## Decision

- keep the change because it is correct, simple, and clearly better in isolation
- do not expect this branch alone to move the submission into the next score tier

## Next branch

The next likely win is not another micro-kernel tweak. It is systematic search-frontier exploration:

1. compare existing artifact topologies under the current kernel
2. sweep `beamLevel1`, `beamLevel2`, and `rerankCount`
3. promote only candidates that improve the recall/latency frontier
