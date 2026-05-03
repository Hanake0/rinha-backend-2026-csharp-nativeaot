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
  - systematic search frontier work from the promoted `512x64` artifact baseline
- latest completed experiment:
  - new `512x64` hierarchical artifact plus beam/rerank frontier sweep under the official evaluator and constrained compose
  - result: materially better full-stack tradeoff than the old `512x32` default, but still short of `0 FP / 0 FN` and `< 1 ms p99`

## Current default submission shape

- load balancer: `nginx:1.27-alpine`
- API runtime: `.NET 10 NativeAOT`
- dataset artifact: `runtime-data/`
- runtime-data provenance:
  - promoted source build directory: `runtime-data-512x64-s524k`
  - topology: `L1 = 512`, `L2 per L1 = 64`
  - training sample size: `524,288`
  - k-means iterations: `12`
- search runtime:
  - `beamLevel1 = 8`
  - `beamLevel2 = 48`
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
  - `FP = 6`
  - `FN = 12`
  - detection score `2509.96`
  - search latency after rerank lookup:
    - `p50 = 114.2 us`
    - `p95 = 590.7 us`
    - `p99 = 713.1 us`
    - `mean = 240.4 us`
- full compliant stack, current default compose:
  - latest validated run: `p99 = 1.89 ms`, final score `5233.41`
  - best observed run: `p99 = 1.89 ms`, final score `5234.56`
  - detection result: `FP = 6`, `FN = 12`, `http_errors = 0`
- best evaluator-side detection candidate on the current artifact family:
  - config: `beamLevel1 = 10`, `beamLevel2 = 96`, `rerankCount = 48`
  - `FP = 1`
  - `FN = 3`
  - detection score `2687.58`
  - evaluator search latency `p99 = 1292.1 us`
  - not promoted because it pushed the stack further away from the latency target

## Latest profiling anchors

- profiling command:
  - `powershell -ExecutionPolicy Bypass -File scripts\profile-compose.ps1 -RuntimeDataDir runtime-data-512x64-s524k -BeamLevel1 8 -BeamLevel2 48 -RerankCount 48 -TopK 5 -ApprovalThreshold 0.6 -HttpParserMode Manual -HttpIoQueueCount 0 -HttpInlineScheduling true -HttpNoDelay true -LbCpus 0.20 -ApiCpus 0.40 -LbMemLimit 48m -ApiMemLimit 151m -UseLastTransactionPartitionPruning:$true`
- API1 sampled profile:
  - `bodyReadUs p50/p99 = 1.82 / 6.01`
  - `parseUs p50/p99 = 2.39 / 4.91`
  - `vectorizeUs p50/p99 = 0.35 / 1.05`
  - `searchUs p50/p99 = 280.22 / 1457.23`
  - `responseWriteUs p50/p99 = 36.99 / 60.87`
  - `totalUs p50/p99 = 327.36 / 1507.60`
- API2 sampled profile:
  - `bodyReadUs p50/p99 = 1.79 / 7.71`
  - `parseUs p50/p99 = 2.38 / 5.08`
  - `vectorizeUs p50/p99 = 0.37 / 1.42`
  - `searchUs p50/p99 = 204.70 / 1526.61`
  - `responseWriteUs p50/p99 = 37.14 / 62.46`
  - `totalUs p50/p99 = 251.55 / 1570.78`
- memory under load:
  - `api1` max observed `16.77 MiB / 151 MiB`
  - `api2` max observed `16.89 MiB / 151 MiB`
  - `lb` max observed `5.38 MiB / 48 MiB`
- direct implication:
  - parser, vectorizer, and write path are already cheap
  - rerank is no longer the dominant hotspot
  - the next real win has to come from search-path selectivity, candidate pruning, or a structurally better index/search algorithm

See:

- `benchmarks/results/stack/2026-05-03-stack-summary.md`
- `benchmarks/results/stack/2026-05-03-512x64-frontier.md`
- `benchmarks/results/stack/2026-05-03-profile-breakdown.md`
- `benchmarks/results/stack/2026-05-03-last-transaction-partitioning.md`
- `benchmarks/results/stack/2026-05-03-candidate-reservoir.md`
- `benchmarks/results/stack/2026-05-03-rerank-half-lookup.md`
- `artifacts/compose-k6/k6-workdir/test/results.json`

## Current caveats

- The stack is compliant and self-contained, but it is still well above the `0.5 ms` target.
- The present bottleneck is the search path under the real `900 req/s` full-stack run, not correctness parsing, response encoding, or memory pressure.
- Best measured submission score is `5234.56`, and the latest validation rerun was `5233.41`; both are still short of the `6000` target.
