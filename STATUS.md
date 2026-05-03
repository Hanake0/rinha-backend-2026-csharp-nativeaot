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

- `f4e9de4` `⚙️ perf(index): promote 256x128 topology frontier`
- `6379af1` `🐛 fix(api): use source-generated profile serialization for nativeaot`
- `9d16d52` `✨ feat(tooling): export traced evaluator sweep metrics`
- `f65576d` `⚡ perf(search): accelerate f16 rerank distance`

## Current exploration stage

- stage: `search-path optimization`
- active objective:
  - `FP = 0`
  - `FN = 0`
  - full-stack `p99 < 1.0 ms`, stretch `p99 < 0.5 ms`
- current measured blocker:
  - service-side tail is down to roughly `0.64-0.68 ms`
  - the remaining stack gap is split between search selectivity and LB/network-visible overhead
- next candidate branch:
  - LB/runtime overhead reduction
  - metric/index experiments that close the remaining `1 FP / 2 FN`
  - alternate ANN structures only if they can beat the new pruning baseline under constrained compose
- latest completed experiment:
  - optional q8 leaf-radius pruning on top of the promoted `256x128` topology, followed by evaluator and constrained compose frontier sweeps
  - result: new measured best full-stack score on `8/128/48` with materially lower stack p99 and better recall

## Current default submission shape

- load balancer: `nginx:1.27-alpine`
- API runtime: `.NET 10 NativeAOT`
- dataset artifact: `runtime-data/`
- runtime-data provenance:
  - promoted source build directory: `runtime-data-256x128-radii-s524k`
  - topology: `L1 = 256`, `L2 per L1 = 128`
  - training sample size: `524,288`
  - k-means iterations: `12`
- search runtime:
  - `beamLevel1 = 8`
  - `beamLevel2 = 128`
  - `rerankCount = 48`
  - `topK = 5`
  - `approvalThreshold = 0.6`
  - `useLeafRadiusPruning = true`
  - `useLastTransactionPartitionPruning = true`
- transport tuning:
  - `Runtime:Http:IoQueueCount = 0`
  - `Runtime:Http:UnsafePreferInlineScheduling = true`
  - `Runtime:Http:NoDelay = true`
- constrained resource split:
  - `lb = 0.15 CPU / 48 MB`
  - `api1 = 0.425 CPU / 151 MB`
  - `api2 = 0.425 CPU / 151 MB`

## Latest benchmark anchors

- reference parser: `~569-656 ns`
- vectorizer: `~19.5-20.4 ns`
- q8 encode: `~15.7 ns`
- f16 encode: `~27.8 ns`
- exact flat search, 4,096 vectors: `~84.0 us`
- approximate hierarchical search, 4,096 vectors: `~3.19 us`
- end-to-end detection pipeline, exact: `~96.2 us`
- end-to-end detection pipeline, approximate: `~3.57 us`
- full-corpus evaluator, current default artifact:
  - `FP = 1`
  - `FN = 2`
  - detection score `2729.07`
  - search latency after rerank lookup:
    - latest validated `ServiceManual`: `p50 = 104.0 us`, `p95 = 214.1 us`, `p99 = 505.1 us`, `mean = 117.0 us`
    - best observed `ManualParser` frontier: `p50 = 104.1 us`, `p95 = 210.2 us`, `p99 = 390.8 us`, `mean = 114.4 us`
- full compliant stack, current default compose:
  - latest validated run: `p99 = 1.13 ms`, final score `5675.38`
  - best observed run: `p99 = 1.10 ms`, final score `5688.47`
  - detection result: `FP = 1`, `FN = 2`, `http_errors = 0`
- best evaluator-side detection candidate on the current artifact family:
  - config: `beamLevel1 = 8`, `beamLevel2 = 128`, `rerankCount = 48`
  - `FP = 1`
  - `FN = 2`
  - detection score `2729.07`
  - best observed evaluator search latency `p99 = 390.8 us`
  - latest validated service-path evaluator search latency `p99 = 505.1 us`
  - best observed compose result: `p99 = 1.10 ms`, final score `5688.47`
  - promoted because it improved both correctness and stack p99 on the constrained run

## Latest profiling anchors

- profiling command:
  - `powershell -ExecutionPolicy Bypass -File scripts\profile-compose.ps1 -RuntimeDataDir runtime-data-256x128-radii-s524k -BeamLevel1 8 -BeamLevel2 96 -RerankCount 48 -TopK 5 -ApprovalThreshold 0.6 -UseLeafRadiusPruning:$true -HttpParserMode Manual -HttpIoQueueCount 0 -HttpInlineScheduling true -HttpNoDelay true -LbCpus 0.15 -ApiCpus 0.425 -LbMemLimit 48m -ApiMemLimit 151m -UseLastTransactionPartitionPruning:$true`
- API1 sampled profile:
  - `bodyReadUs p50/p99 = 1.79 / 7.28`
  - `parseUs p50/p99 = 2.29 / 5.12`
  - `vectorizeUs p50/p99 = 0.36 / 1.34`
  - `searchUs p50/p99 = 182.30 / 621.20`
  - `responseWriteUs p50/p99 = 36.16 / 52.78`
  - `totalUs p50/p99 = 225.71 / 679.13`
- API2 sampled profile:
  - `bodyReadUs p50/p99 = 1.78 / 6.27`
  - `parseUs p50/p99 = 2.31 / 4.95`
  - `vectorizeUs p50/p99 = 0.37 / 1.42`
  - `searchUs p50/p99 = 190.18 / 601.50`
  - `responseWriteUs p50/p99 = 36.07 / 61.63`
  - `totalUs p50/p99 = 235.02 / 641.54`
- memory under load:
  - `api1` max observed `17.20 MiB / 151 MiB`
  - `api2` max observed `16.68 MiB / 151 MiB`
  - `lb` max observed `5.68 MiB / 48 MiB`
- direct implication:
  - parser, vectorizer, and write path are already cheap
  - search still dominates the service-side tail
  - memory pressure is not the current limiter
  - external LB/network-visible overhead is now material enough to benchmark directly

## Evidence

- `benchmarks/results/stack/2026-05-03-stack-summary.md`
- `benchmarks/results/stack/2026-05-03-leaf-radius-pruning.md`
- `benchmarks/results/stack/2026-05-03-profile-breakdown.md`
- `artifacts/evaluator-sweeps/leaf-radius-frontier.csv`
- `artifacts/evaluator-sweeps/leaf-radius-rerank-frontier.csv`
- `artifacts/evaluator-sweeps/leaf-radius-wide-frontier.csv`
- `artifacts/compose-k6/k6-workdir/test/results.json`

## Current caveats

- The stack is compliant and self-contained, but it is still above the `0.5 ms` target.
- The present bottleneck is no longer parsing or memory pressure. It is search selectivity plus stack-visible transport overhead.
- Best measured submission score is `5688.47`; it is still short of the `6000` target.
