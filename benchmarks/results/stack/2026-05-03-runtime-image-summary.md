# 2026-05-03 Runtime Image Branch

## Goal

Check whether smaller API runtime images can reduce end-to-end stack latency without sacrificing exactness.

Variants tested:

- `mcr.microsoft.com/dotnet/runtime-deps:10.0-noble`
- `mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled`
- musl static NativeAOT API on `scratch`

All comparisons used the same nginx load balancer, the same API code, and the accepted stable runtime-data family:

- `runtime-data-256x128-radii-f32-stable-s524k`

## Important Note

An initial rerun against the default `runtime-data` directory showed `FP = 1` and `FN = 1` across multiple image variants.

That turned out to be an artifact mismatch, not an image regression. The accepted exact baseline is tied to `runtime-data-256x128-radii-f32-stable-s524k`, and exactness returned immediately once the quick screen used that directory.

## Results

Quick-screen harness:

- `scripts/benchmark-quick-compose.ps1`
- `900 req/s` for `12s`

### Control: `10.0-noble`

- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- `p99 = 35.88 ms`
- final score `4445.18`

### Musl static API on `scratch`

- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- `p99 = 44.31 ms`
- final score `4353.49`

### `10.0-noble-chiseled`

- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- `p99 = 58.03 ms`
- final score `4236.32`

## Interpretation

- Smaller images did not translate into a faster constrained stack.
- The accepted `10.0-noble` control remained the best end-to-end result of the three.
- The musl static `scratch` variant is technically viable, but it was slower than the control.
- The `noble-chiseled` variant was the worst of the tested image family.

## Later Follow-up

This note captured the first quick-screen read on the image branch, but it was not the final word.

Later work corrected two important issues:

- the Alpine scratch build was updated to use the current `10.0.203` SDK path instead of the stale `10.0.103` image SDK
- the startup path was changed to warm the real request flow before `/ready`

With those fixes in place, later full official runs on the same stable artifact family showed:

- `scratch`: exact `0 / 0`, official `6000` reached
- `noble`: exact `0 / 0`, official `6000` also reached on another run

So the original quick-screen conclusion in this file is now obsolete for submission decisions.

Current interpretation:

- `noble-chiseled` is still rejected
- `scratch` is viable and promoted as the current default submission image path
- `noble` remains a valid fallback control because both families are close enough that host variance can move the reported `p99` around the `1.0 ms` threshold
