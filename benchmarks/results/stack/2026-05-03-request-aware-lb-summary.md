# 2026-05-03 Request-Aware NativeAOT LB

## Goal

Revisit the custom LB branch with the user constraint that a transport or topology idea is not rejected after a naive one-pass prototype.

This pass changes two things relative to the earlier rejected native LB branch:

- balancing is now per request, not per client connection
- the branch now has a short-screen harness that still runs at `900 req/s`

## Implemented variants

- `docker-compose.native-lb.yml`
  - NativeAOT LB on external TCP, backend TCP
- `docker-compose.native-lb-uds.yml`
  - NativeAOT LB on external TCP, backend UDS

The LB keeps backend connections open and forwards whole HTTP/1.1 requests and responses at request granularity.

Follow-up optimization in this same branch:

- backend connections are now pooled across clients instead of being kept per client/backend pair
- pool size and prewarm count are configurable via `LB_BACKEND_POOL_SIZE` and `LB_BACKEND_PREWARM_CONNECTIONS`

## Fast-screen harness

New helper:

- `scripts/benchmark-quick-compose.ps1`

Its defaults are intentionally high pressure, not a low-rate smoke test:

- `startRate = 900`
- `targetRate = 900`
- `stageDuration = 12s`
- `gracefulStop = 2s`

This keeps short screens aligned with the main contest pressure while cutting iteration time sharply versus the full `120s` official-style pass.

## Short-screen results

Dataset and runtime settings:

- runtime-data: `runtime-data-256x128-radii-f32-stable-s524k`
- `beamLevel1 = 8`
- `beamLevel2 = 128`
- `rerankCount = 48`
- `boundaryRerankCount = 48`
- `topK = 5`
- `approvalThreshold = 0.6`
- `useLeafRadiusPruning = true`
- `useLastTransactionPartitionPruning = false`
- resource split: `0.15 / 0.425 / 0.425`

### Baseline short screen

- compose: `docker-compose.yml`
- short result: `p99 = 43.83 ms`
- detections: `0 / 0`

### Request-aware LB over TCP backends

- compose: `docker-compose.native-lb.yml`
- first request-aware result: `p99 = 226.98 ms`
- pooled-backend follow-up: `p99 = 108.82 ms`
- detections: `0 / 0`

### Request-aware LB over UDS backends

- compose: `docker-compose.native-lb-uds.yml`
- clean sequential validation succeeded
- first request-aware result: `p99 = 104.81 ms`
- pooled-backend follow-up: `p99 = 105.79 ms`
- pooled UDS sweep:
  - `pool = 4`: `277.53 ms`
  - `pool = 8`: `250.66 ms`
  - `pool = 16`: `181.18 ms`
  - `pool = 32`: `108.09 ms`
  - `pool = 64`: best observed `86.51 ms`
  - larger pool reruns (`80-192`) were noisy and did not beat that best observed point consistently

## Current reading

- the quick-screen harness is good enough to reject obvious losers before a full `120s` run
- the `900 req/s` quick harness is doing its job: obvious LB regressions now fail fast
- shared backend pooling materially reduced the custom TCP LB tail, so this branch was worth revisiting
- backend UDS remains the better internal transport for this custom LB family, but it is still far behind nginx on the same quick screen
- the best observed quick-screen point on this branch is still nowhere near the nginx quick baseline (`43.83 ms`)
- this branch stays open only for deeper LB-path optimization or for reuse in later shared-search experiments

## Next justified steps

1. use the quick harness first for LB and IPC hot-path edits
2. only run the full official-style benchmark on branches that are at least neutral on the quick screen
3. if the custom LB branch continues, focus on:
   - fewer copies in the LB request/response path
   - less buffer shuffling
   - cheaper backend response parsing
   - comparing UDS backend transport against any future shared-search branch
