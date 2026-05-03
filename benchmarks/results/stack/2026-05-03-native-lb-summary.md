# 2026-05-03 Native L4 Load Balancer Summary

## Goal

Test whether replacing `nginx` with a purpose-built NativeAOT tcp proxy could remove enough ingress overhead to materially close the remaining gap to `p99 < 0.5 ms`.

The APIs and search settings were intentionally left unchanged. This was an ingress-only branch.

## Implementation tested

- new service: `Rinha2026.LoadBalancer`
- transport model:
  - client tcp connection accepted by the custom LB
  - backend selected round-robin per client connection
  - one backend tcp connection opened for that client connection
  - raw bidirectional byte forwarding until either side closed
- no HTTP parsing at the LB layer

This is effectively a simple L4 proxy with persistent keep-alive behavior delegated to the client/backend sockets.

## Benchmark configuration

- compose file: `docker-compose.native-lb.yml`
- runtime-data: `runtime-data-256x128-radii-f32-stable-s524k`
- `beamLevel1 = 8`
- `beamLevel2 = 128`
- `rerankCount = 48`
- `boundaryRerankCount = 48`
- `topK = 5`
- `approvalThreshold = 0.6`
- `useLeafRadiusPruning = true`
- `useLastTransactionPartitionPruning = false`
- `Runtime:Http:UnsafePreferInlineScheduling = true`
- resource split:
  - `lb = 0.15 CPU / 48 MiB`
  - `api1 = 0.425 CPU / 151 MiB`
  - `api2 = 0.425 CPU / 151 MiB`

## Result

- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- `p99 = 67.74 ms`
- final score `4169.15`

Observed runtime memory:

- `lb ~ 3.9 MiB`
- `api1 ~ 24.7 MiB`
- `api2 ~ 25.3 MiB`

## Interpretation

This branch is a hard rejection.

The failure mode is not correctness, CPU cap, or memory pressure. It is queueing.

Because balancing happened per client connection rather than per request, keep-alive traffic accumulated badly enough to destroy the tail even though both APIs stayed active and exact.

## Conclusion

Do not pursue naive L4 connection balancing further.

If a custom LB is revisited later, it must be request-aware and it must beat `nginx` under the real constrained compose benchmark, not just in a micro-benchmark.
