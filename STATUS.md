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
- [x] Final stack validation and submission packaging

## Latest validated commits

- `94b69b0` `✨ feat(index): add binary corpus artifact builder`
- `5bf2722` `✨ feat(search): add exact flat artifact baseline`
- `21aa150` `✨ feat(index): add hierarchical beam ivf search path`
- `4be4071` `✨ feat(api): add fraud scoring runtime and endpoint`

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
- full compliant stack, current default compose:
  - latest validated run: `p99 = 2.38 ms`, final score `5157.29`
  - best observed run: `p99 = 2.30 ms`, final score `5172.24`
  - historical one-off on a nearby config: `p99 = 2.25 ms`, final score `5176.54` with `8/32/64`

See:

- `benchmarks/results/stack/2026-05-03-stack-summary.md`
- `artifacts/compose-k6/k6-workdir/test/results.json`

## Current caveats

- The stack is compliant and self-contained, but it is still well above the `0.5 ms` target.
- The present bottleneck is the search path under the real `900 req/s` full-stack run, not correctness or startup stability.
- Best measured submission score is in the `~5.17k` range, not the `6k` target.
