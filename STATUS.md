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
- [x] Stable full-precision rerank artifact support
- [x] Official generator-compatible round4 vectorization
- [x] Constrained transport comparison harness

## Latest validated commits

- `189b706` `🧪 feat(tooling): add constrained transport comparison harness`
- `29ac822` `🐛 fix(vectorization): match official round4 semantics`
- `dc3fbca` `📦 feat(index): add stable full-precision rerank support`
- `16360b3` `🧱 chore(docker): trim api build context`

## Current exploration stage

- stage: `search-path optimization`
- active objectives:
  - `FP = 0`
  - `FN = 0`
  - full-stack `p99 < 1.0 ms`, stretch `p99 < 0.5 ms`
- current accepted baseline:
  - corrected stable artifact family is now exact at `0/0`
  - constrained stack is reproducibly in the `1.35-1.38 ms` range
- latest rejected branch:
  - AVX-specific f32 rerank distance path
  - micro-benchmarks improved, but constrained compose regressed to `1.47 ms`
- next candidate branch:
  - exact-safe q8 scan pruning and thresholded early-exit
  - candidate-selection cost reduction before rerank
  - only then deeper transport/runtime surgery if search-side wins stall

## Current validated candidate shape

- load balancer: `nginx:1.27-alpine`
- API runtime: `.NET 10 NativeAOT`
- validated runtime-data directory:
  - `runtime-data-256x128-radii-f32-stable-s524k`
- artifact properties:
  - topology: `L1 = 256`, `L2 per L1 = 128`
  - training sample size: `524,288`
  - k-means iterations: `12`
  - rerank corpus:
    - `vectors.f16.bin`
    - `vectors.f32.bin`
    - `vectors.original.ids.bin`
- search runtime:
  - `beamLevel1 = 8`
  - `beamLevel2 = 128`
  - `rerankCount = 48`
  - `topK = 5`
  - `approvalThreshold = 0.6`
  - `useLeafRadiusPruning = true`
  - `useLastTransactionPartitionPruning = false` for the current stack baseline
- transport/runtime tuning:
  - `Runtime:Http:IoQueueCount = 0`
  - `Runtime:Http:UnsafePreferInlineScheduling = true`
  - `Runtime:Http:NoDelay = true`
  - `DOTNET_PROCESSOR_COUNT = 1`
  - `DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS = 1`
  - `DOTNET_SYSTEM_NET_SOCKETS_THREAD_COUNT = 1`
- constrained resource split:
  - `lb = 0.15 CPU / 48 MB`
  - `api1 = 0.425 CPU / 151 MB`
  - `api2 = 0.425 CPU / 151 MB`

## Latest benchmark anchors

- full-corpus evaluator, corrected stable artifact:
  - config: `8 / 128 / 48`, `leafRadius = true`, `lastTxPartition = false`
  - `FP = 0`
  - `FN = 0`
  - detection score `3000`
  - search latency:
    - `p50 = 104.4 us`
    - `p95 = 211.4 us`
    - `p99 = 510.6 us`
    - `mean = 117.4 us`
- full-corpus evaluator, partition-pruning experiment:
  - config: `8 / 128 / 48`, `leafRadius = true`, `lastTxPartition = true`
  - `FP = 0`
  - `FN = 0`
  - search `p99 = 524.5 us`
  - kept as exact but not promoted
- full compliant stack, corrected stable baseline:
  - command family: `benchmark-official-compose.ps1`
  - runtime-data: `runtime-data-256x128-radii-f32-stable-s524k`
  - `p99 = 1.38 ms`
  - `FP = 0`
  - `FN = 0`
  - `http_errors = 0`
  - final score `5860.13`
- best observed stack on the corrected stable family:
  - `p99 = 1.35 ms`
  - detection `0 / 0`
  - final score `5869.95`
- rejected AVX branch:
  - evaluator stayed exact
  - compose degraded to `1.47 ms`, final `5832.55`

## Latest profiling anchors

- corrected stable path, representative service-side profile:
  - API1 total `p99 ~ 713.9 us`
  - API2 total `p99 ~ 758.9 us`
  - search remains the dominant in-service stage
- memory under constrained load:
  - `api1` max observed `~17.2 MiB / 151 MiB`
  - `api2` max observed `~17.3 MiB / 151 MiB`
  - `lb` max observed `~3.7-6.7 MiB / 48 MiB`
- implication:
  - memory pressure is still not the limiter
  - parser/vectorizer are already cheap
  - the remaining gap is dominated by search tail plus stack-visible transport/runtime overhead

## Evidence

- `benchmarks/results/stack/2026-05-03-round4-stable-summary.md`
- `benchmarks/results/stack/2026-05-03-profile-breakdown.md`
- `artifacts/evaluator-round4-hierarchical-f32-stable-noavx.json`
- `artifacts/evaluator-round4-hierarchical-f32-stable-partition-on.json`
- `artifacts/compose-k6-round4-f32-inline-runtime1-noavx/k6-workdir/test/results.json`
- `artifacts/compose-k6-round4-f32-inline-runtime1-avx-rerun/k6-workdir/test/results.json`

## Current caveats

- The stack is now exact on the official evaluator path, but it is still above the `0.5 ms` target.
- The accepted baseline is short of the `6000` target by roughly `140` points on the latest reproduced run.
- The fastest rejected branch so far was not the transport layer; it was a measurement trap from a micro-benchmark-only win.
