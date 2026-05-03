# Implementation Status

## Stage checklist

- [x] Solution scaffolded and validated
- [x] Runtime configuration and `/ready`
- [x] Correctness parser and vectorizer
- [x] Flat binary corpus artifacts
- [x] Exact flat search baseline
- [x] Hierarchical beam IVF build and query path
- [x] Fraud scoring runtime and `/fraud-score`
- [x] Dockerized NativeAOT API image
- [x] 1 LB + 2 API compose stack
- [x] Constrained compose benchmark harness
- [x] Real-stack request-stage and memory profiling harness
- [x] Final stack validation and submission packaging

## Latest validated commits

- `94b69b0` `✨ feat(index): add binary corpus artifact builder`
- `5bf2722` `✨ feat(search): add exact flat artifact baseline`
- `21aa150` `✨ feat(index): add hierarchical beam ivf search path`
- `4be4071` `✨ feat(api): add fraud scoring runtime and endpoint`

## Current exploration stage

- stage: `search-path optimization`
- active objective:
  - `FP = 0`
  - `FN = 0`
  - full-stack `p99 < 1.0 ms`, stretch `p99 < 0.5 ms`
- current measured blocker:
  - search dominates the service-side tail under the real `900 req/s` constrained stack
- next candidate branch:
  - systematic artifact and beam/rerank sweep to improve the recall/latency frontier
- latest completed experiment:
  - lookup-table exact rerank on the f16 path with a fixed `16`-lane fast path
  - result: strong microbenchmark win, negligible memory cost, modest service-side improvement, but no material full-stack score movement

## Current default submission shape

- load balancer: `nginx:1.27-alpine`
- API runtime: `.NET 10 NativeAOT`
- dataset artifact: `runtime-data/`
- runtime-data provenance:
  - topology: `L1 = 512`, `L2 per L1 = 32`
  - training sample size: `524,288`
  - k-means iterations: `12`
- search runtime:
  - `beamLevel1 = 10`
  - `beamLevel2 = 32`
  - `rerankCount = 48`
  - `topK = 5`
  - `approvalThreshold = 0.6`
- transport tuning:
  - `Runtime:Http:IoQueueCount = 0`
  - `Runtime:Http:UnsafePreferInlineScheduling = true`
  - `Runtime:Http:NoDelay = true`
- constrained resource split:
  - `lb = 0.20 CPU / 48 MB`
  - `api1 = 0.40 CPU / 151 MB`
  - `api2 = 0.40 CPU / 151 MB`

## Latest benchmark anchors

- reference parser: `~569–656 ns`
- vectorizer: `~19.5–20.4 ns`
- q8 encode: `~15.7 ns`
- f16 encode: `~27.8 ns`
- exact flat search, 4,096 vectors: `~84.0 us`
- approximate hierarchical search, 4,096 vectors: `~3.19 us`
- end-to-end detection pipeline, exact: `~96.2 us`
- end-to-end detection pipeline, approximate: `~3.57 us`
- full-corpus evaluator, current default artifact:
  - `FP = 5`
  - `FN = 10`
  - detection score `2533.11`
  - search latency after rerank lookup:
    - `p50 = 131.9 us`
    - `p95 = 764.4 us`
    - `p99 = 981.8 us`
    - `mean = 297.5 us`
- full compliant stack, current default compose:
  - latest validated run: `p99 = 2.31 ms`, final score `5169.15`
  - best observed run: `p99 = 2.30 ms`, final score `5172.24`

## Latest profiling anchors

- profiling command:
  - `powershell -ExecutionPolicy Bypass -File scripts\profile-compose.ps1 -BeamLevel1 10 -BeamLevel2 32 -RerankCount 48 -TopK 5 -ApprovalThreshold 0.6 -HttpParserMode Manual -HttpIoQueueCount 0 -HttpInlineScheduling true -HttpNoDelay true -LbCpus 0.20 -ApiCpus 0.40 -LbMemLimit 48m -ApiMemLimit 151m`
- API1 sampled profile:
  - `bodyReadUs p50/p99 = 1.83 / 15.35`
  - `parseUs p50/p99 = 2.38 / 3.60`
  - `vectorizeUs p50/p99 = 0.36 / 1.22`
  - `searchUs p50/p99 = 345.77 / 1804.09`
  - `responseWriteUs p50/p99 = 36.66 / 55.34`
  - `totalUs p50/p99 = 397.20 / 1833.73`
- API2 sampled profile:
  - `bodyReadUs p50/p99 = 1.78 / 3.57`
  - `parseUs p50/p99 = 2.36 / 4.23`
  - `vectorizeUs p50/p99 = 0.36 / 1.25`
  - `searchUs p50/p99 = 232.41 / 1823.24`
  - `responseWriteUs p50/p99 = 36.66 / 59.90`
  - `totalUs p50/p99 = 273.34 / 1864.78`
- memory under load:
  - APIs stayed in the mid-teen MiB range under load
  - LB stayed around `4-5 MiB / 48 MiB`
- direct implication:
  - parser, vectorizer, and write path are already cheap
  - rerank is no longer the obvious hotspot
  - the next real win has to come from search-path selectivity and scan-count reduction
  - historical one-off on a nearby config: `p99 = 2.25 ms`, final score `5176.54` with `8/32/64`

See:

- `benchmarks/results/stack/2026-05-03-stack-summary.md`
- `benchmarks/results/stack/2026-05-03-profile-breakdown.md`
- `benchmarks/results/stack/2026-05-03-last-transaction-partitioning.md`
- `benchmarks/results/stack/2026-05-03-candidate-reservoir.md`
- `benchmarks/results/stack/2026-05-03-rerank-half-lookup.md`
- `artifacts/compose-k6/k6-workdir/test/results.json`

## Current caveats

- The stack is compliant and self-contained, but it is still well above the `0.5 ms` target.
- The present bottleneck is the search path under the real `900 req/s` full-stack run, not correctness, JSON parsing, or startup stability.
- Best measured submission score is in the `~5.17k` range, not the `6k` target.
