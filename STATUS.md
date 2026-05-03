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

- stage: `search tail reduction under the exact stable artifact family`
- active objectives:
  - `FP = 0`
  - `FN = 0`
  - sustain `6000` on the official benchmark path
  - stretch `p99 < 0.5 ms`
- current accepted baseline:
  - corrected stable artifact family is exact at `0 / 0`
  - current scratch-default stack has now hit the full official `6000` score band
- latest rejected branches:
  - AVX-specific f32 rerank distance path
  - adaptive boundary rerank fallback promoted from `32 -> 48`
  - unix-domain-socket transport stack
  - in-process artifact memory promotion
  - shared Docker-managed data volume seeded by a helper container
  - shared `searchd` pivot behind nginx
  - native AOT tcp L4 load balancer
  - class-aware exact fraud-count search
  - raw socket HTTP API server
  - API runtime-base swap to `noble-chiseled`
  - helper-container pre-ready warm-up through `nginx`
- next candidate branch:
  - algorithmic search-side cuts that preserve the exact `0 / 0` decision surface
  - distance and candidate-pruning experiments only if they can be validated against the official evaluator
  - only revisit a custom LB if it is request-aware and it can prove a win under the same envelope
  - use the new `900 req/s` short-screen harness to reject obvious stack regressions before spending a full `120s` compose run

## Current validated candidate shape

- load balancer: `nginx:1.27-alpine`
- API runtime: `.NET 10 NativeAOT` musl static on `scratch`
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
  - `boundaryRerankCount = 48`
  - `topK = 5`
  - `approvalThreshold = 0.6`
  - `useLeafRadiusPruning = true`
  - `useLastTransactionPartitionPruning = false` for the current stack baseline
- transport/runtime tuning:
  - `Runtime:Http:UnsafePreferInlineScheduling = true`
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
- latest reproduced stable baseline:
  - same corrected stable family and CPU split
  - `p99 = 1.43 ms`
  - detection `0 / 0`
  - final score `5845.68`
- current promoted startup-warm stack:
  - default `docker-compose.yml` now uses the musl static `scratch` API image path
  - built-in startup warm-up faults in mmap-backed artifacts, runs real `TryHandle(...)` warm-up payloads, then warms the loopback HTTP `/fraud-score` path before `/ready`
  - full official run landed at `p99 = 0.93 ms`, `FP = 0`, `FN = 0`, `http_errors = 0`, final `6000`
  - later full official scratch reruns landed at `p99 = 0.96 ms` and `1.03 ms`; a matching noble control on the same current code landed at `1.04 ms`
  - conclusion: both runtime families are near the `1.0 ms` cut line on this workstation, but scratch is now a valid promoted default rather than a rejected image branch
- rejected AVX branch:
  - evaluator stayed exact
  - compose degraded to `1.47 ms`, final `5832.55`
- adaptive boundary rerank branch:
  - evaluator recovered exactness with `rerankCount = 32`, `boundaryRerankCount = 48`
  - compose regressed to `p99 = 1.41 ms`, final `5850.77`
  - kept as an experiment, not promoted
- short transport knob check:
  - forcing `Runtime:Http:IoQueueCount = 0` was slightly worse than the short exact baseline
  - not promoted
- memory-mode branch:
  - `PromoteMetadata` looked slightly better in host-side micro and evaluator runs
  - constrained compose regressed sharply to `2.04 ms`
  - `PromoteMetadataAndQuantizedVectors` regressed further to `3.22 ms`
  - rejected
- shared `searchd` branch:
  - rules are permissive enough for `lb + api1 + api2 + searchd`
  - first prototype used `API(parse/vectorize) -> searchd(uds binary query)`
  - exactness stayed `0 / 0`
  - constrained compose regressed to `40.99 ms` and then `433.96 ms` on a frontend-heavier CPU split
  - rejected in its current form
- native L4 LB branch:
  - first prototype used a native AOT tcp proxy with round-robin backend assignment per client connection
  - exactness stayed `0 / 0`
  - constrained compose regressed to `67.74 ms`, final `4169.15`
  - rejected because connection-level balancing introduced catastrophic queueing under keep-alive load
- class-aware exact fraud-count branch:
  - reformulated the query path around the final fraud-count decision for `topK = 5`
  - evaluator remained exact across `rerankCount = 10, 12, 16, 20, 24, 32, 40, 48`
  - best observed evaluator search latency still regressed to about `p99 = 1.53 ms`
  - rejected because the extra frontier bookkeeping cost dominated any rerank reduction
- shared Docker-managed data volume branch:
  - added a one-shot `data-init` helper that copied runtime data into a named Docker volume
  - both APIs memory-mapped that shared Linux-side volume instead of a direct host bind mount
  - full stack stayed exact at `0 / 0` and landed at `p99 = 1.38 ms`, final `5859.90`
  - profile comparison was mixed across replicas and did not yield a decisive end-to-end improvement
  - kept as an experiment, not promoted
- raw socket HTTP API branch:
  - replaced the Kestrel request path with a custom HTTP/1.1 keep-alive socket server for `/ready`, `/fraud-score`, and profile endpoints
  - first constrained compose run stayed exact at `0 / 0` but regressed to `p99 = 1.46 ms`, final `5834.92`
  - a follow-up pass removed the per-request body copy and ASCII string parsing, but regressed further to `p99 = 1.79 ms`, final `5747.19`
  - rejected because the custom server underperformed Kestrel on the only metric that matters: end-to-end constrained stack latency
- request-aware custom LB revival:
  - replaced the earlier connection-level native proxy with per-request round-robin and persistent backend connections
  - the new `scripts/benchmark-quick-compose.ps1` harness now screens at `900 req/s` for `12s`, not at a low-rate ramp
  - current quick-screen anchor: nginx baseline `43.83 ms`, first request-aware LB over backend TCP `226.98 ms`, first request-aware LB over backend UDS `104.81 ms`, all exact at `0 / 0`
  - a follow-up pass pooled backend sockets across clients and improved the custom TCP branch to `108.82 ms`
  - pooled UDS pool-size sweeps found a best observed short-screen point of `86.51 ms` around pool size `64`, but still far behind nginx
  - a ready-only isolation benchmark showed `nginx + kestrel = 0.49 ms` versus `custom LB + backend UDS + kestrel = 68.59 ms` at the same `900 req/s` pressure
  - non-stripped NativeAOT symbol inspection shows the current LB binary is still dominated by async state-machine machinery in `HandleClientAsync`, `TryProxyRequestAsync`, `TryProxyResponseAsync`, `SendAllAsync`, and `RentAsync`
  - conclusion: the current async LB implementation family is not salvageable into a winner; any future custom LB should switch to a different lower-level implementation family instead of micro-tweaking the same shape
- API runtime-image branch:
  - validated Ubuntu WSL toolchain for future musl work with `.NET SDK 10.0.201`, `clang 18.1.3`, and the installed `musl-gcc` wrapper
  - first quick reruns against the default `runtime-data` directory showed `FP = 1`, `FN = 1`, but that was traced to using the wrong artifact family instead of a true code or image regression
  - rerunning on the accepted stable artifact family `runtime-data-256x128-radii-f32-stable-s524k` restored exactness for all image variants tested
  - stable quick-screen control with `mcr.microsoft.com/dotnet/runtime-deps:10.0-noble`: `p99 = 35.88 ms`, `FP = 0`, `FN = 0`
  - stable quick-screen musl static API on `scratch`: `p99 = 44.31 ms`, `FP = 0`, `FN = 0`
  - stable quick-screen API on `mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled`: `p99 = 58.03 ms`, `FP = 0`, `FN = 0`
  - after fixing the stale Alpine SDK mismatch and rerunning full official benchmarks on the warmed stack, both `scratch` and `noble` reached the `6000` band; `noble-chiseled` remains rejected
  - conclusion: promote the musl static `scratch` API path as the current submission default and keep `noble` as a fallback control, not as the only accepted runtime base

- helper-container startup warm-up branch:
  - tested a separate pre-ready warm-up helper that routed synthetic bypass-header load through `nginx`
  - the branch was technically made to work, but its first measured short run still regressed badly and never proved better than the lighter built-in startup warm-up
  - rejected in favor of the built-in warm-up path that already produced the official `6000` result

## Latest profiling anchors

- corrected stable path, representative service-side profile:
  - API1 total `p99 ~ 787.3 us`
  - API2 total `p99 ~ 925.4 us`
  - search remains the dominant in-service stage
- corrected stable path, latest full-load profile rerun:
  - API1 total `p99 = 838.3 us`, search `p99 = 783.1 us`
  - API2 total `p99 = 955.1 us`, search `p99 = 882.3 us`
  - body read + parse + vectorize stayed below `15 us` at `p99`
  - response write stayed below `68 us` at `p99`
- direct-vs-lb anchor:
  - earlier direct dual-target replay landed around `1.28 ms`
  - current nginx stack lands around `1.36 ms`
  - the present LB-visible overhead is on the order of `~0.08 ms`
- memory under constrained load:
  - `api1` max observed `~17.2 MiB / 151 MiB`
  - `api2` max observed `~17.2 MiB / 151 MiB`
  - `lb` max observed `~4.9 MiB / 48 MiB`
- implication:
  - memory pressure is still not the limiter
  - the index is already loaded through shared read-only memory maps, not per-request disk reads
  - the remaining gap is dominated by search tail plus stack-visible transport/runtime overhead
  - the next memory experiment is selective hot-structure promotion, not copying the whole corpus into both APIs

## Evidence

- `benchmarks/results/stack/2026-05-03-round4-stable-summary.md`
- `benchmarks/results/stack/2026-05-03-adaptive-transport-memory-summary.md`
- `benchmarks/results/stack/2026-05-03-memory-mode-promotion-summary.md`
- `benchmarks/results/stack/2026-05-03-searchd-prototype-summary.md`
- `benchmarks/results/stack/2026-05-03-native-lb-summary.md`
- `benchmarks/results/stack/2026-05-03-class-aware-summary.md`
- `benchmarks/results/stack/2026-05-03-shared-data-volume-summary.md`
- `benchmarks/results/stack/2026-05-03-raw-http-summary.md`
- `benchmarks/results/stack/2026-05-03-request-aware-lb-summary.md`
- `benchmarks/results/stack/2026-05-03-profile-breakdown.md`
- `artifacts/evaluator-round4-hierarchical-f32-stable-noavx.json`
- `artifacts/evaluator-round4-hierarchical-f32-stable-partition-on.json`
- `artifacts/compose-k6-round4-f32-inline-runtime1-noavx/k6-workdir/test/results.json`
- `artifacts/compose-k6-round4-f32-inline-runtime1-avx-rerun/k6-workdir/test/results.json`

## Current caveats

- The stack is now exact on the official evaluator path, but it is still above the `0.5 ms` target.
- The stack has now reached `6000`, but the current workstation still shows run-to-run variance near the `1.0 ms` score cutoff.
- The next confidence gate should use the exact official script on GitHub Actions or another cleaner Linux host, not just this local workstation.
