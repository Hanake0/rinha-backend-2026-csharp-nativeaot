# 2026-05-03 Raw Socket HTTP API

## Goal

Measure whether Kestrel itself is a meaningful share of the remaining stack-visible tail by replacing the API request path with a custom NativeAOT HTTP/1.1 socket server while keeping:

- the same `nginx` load balancer
- the same exact fraud engine
- the same dataset and frontier
- the same resource limits

The branch implemented only the endpoints that matter for this project:

- `GET /ready`
- `POST /fraud-score`
- optional profile endpoints for the existing harness

## Result

The branch remained exact, but it was slower than the accepted Kestrel baseline.

### Pass 1: first custom server

- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- `p99 = 1.46 ms`
- final score `5834.92`

### Pass 2: remove per-request body copy and ASCII string parsing

- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- `p99 = 1.79 ms`
- final score `5747.19`

Reference accepted exact baseline:

- full stack `p99 = 1.35-1.43 ms`
- final score `5845.68-5869.95`

## Interpretation

- The custom server did not expose a hidden easy win in Kestrel.
- On this workload, the request path inside the hand-rolled server was still worse than the ASP.NET Core NativeAOT path.
- That makes further time on this specific branch hard to justify until search-side work is exhausted.

## Decision

Reject the raw socket server branch.

Kestrel stays as the accepted default API server.
