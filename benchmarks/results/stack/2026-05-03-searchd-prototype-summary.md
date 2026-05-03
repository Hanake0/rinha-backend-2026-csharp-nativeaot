# 2026-05-03 Shared Searchd Prototype Summary

## Rules check

The 2026 docs explicitly allow this topology:

- README: infrastructure requires at least one LB and two API instances
- `ARQUITETURA.md`: you may use databases, middleware, more instances, or whatever else is necessary
- `FAQ.md`: vector databases and other extra containers are allowed

This makes a `lb + api1 + api2 + searchd` architecture rules-compliant as long as:

- the LB only distributes requests
- total CPU and memory across all services stay within `1 CPU / 350 MB`

## Architecture tested

Prototype request path:

1. `nginx` receives `POST /fraud-score`
2. API parses JSON and vectorizes locally
3. API sends the fixed `16 x float32` vector over a persistent unix-domain socket to `searchd`
4. `searchd` runs the current exact stable search frontier and returns a one-byte fraud count
5. API returns the precomputed JSON response

The protocol was intentionally minimal:

- request: fixed `64` bytes
- response: fixed `1` byte

## Why this looked plausible

Centralizing search into one process is the only obvious way to consider loading more of the index into anonymous memory without duplicating it across two APIs.

If it worked, the next step would have been:

- keep APIs tiny
- let `searchd` own the full q8 + f32 corpus
- later test a custom LB in front

## Prototype results

Dataset and search frontier:

- runtime-data: `runtime-data-256x128-radii-f32-stable-s524k`
- `beamLevel1 = 8`
- `beamLevel2 = 128`
- `rerankCount = 48`
- `boundaryRerankCount = 48`
- `topK = 5`
- `approvalThreshold = 0.6`
- `useLeafRadiusPruning = true`
- `useLastTransactionPartitionPruning = false`

### Split A

- `lb = 0.10 CPU / 24 MiB`
- `api1 = 0.10 CPU / 24 MiB`
- `api2 = 0.10 CPU / 24 MiB`
- `searchd = 0.70 CPU / 278 MiB`

Result:

- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- `p99 = 40.99 ms`
- final score `4387.34`

### Split B

- `lb = 0.05 CPU / 16 MiB`
- `api1 = 0.20 CPU / 24 MiB`
- `api2 = 0.20 CPU / 24 MiB`
- `searchd = 0.55 CPU / 286 MiB`

Result:

- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- `p99 = 433.96 ms`
- final score `3362.55`

The second run also showed a `k6` warning about insufficient VUs, which is consistent with the frontend/search hop stalling badly under load.

## Interpretation

This prototype is nowhere near viable.

What it proved:

- the rules path is open
- correctness survives the extra service boundary

What it disproved:

- a naive “shared search service over sockets” implementation is not an acceptable path to `sub-1 ms`, let alone `sub-0.5 ms`

The added hop cost and queueing completely dominated the theoretical memory-layout upside.

## Current conclusion

Keep the idea only as a category, not as the current implementation.

If a shared-index pivot is revisited later, it needs a radically cheaper frontend-to-search coupling than this first prototype. The next branch should instead test a custom LB on top of the current local-search APIs, because that preserves the fast in-process search path and only attacks the measured LB-visible overhead.
