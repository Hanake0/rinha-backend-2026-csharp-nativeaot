# 2026-05-03 Shared Docker-Managed Data Volume

## Goal

Test the user suggestion that we should make fuller use of the available memory without duplicating the full index inside both APIs.

The concrete branch was:

- keep `nginx + 2 APIs`
- keep the exact accepted search path unchanged
- add a one-shot `data-init` helper container
- copy the selected runtime-data directory from the host bind mount into a named Docker volume
- let both APIs `mmap` the shared Docker-managed Linux-side volume at `/app/data`

This isolates data locality from request-path IPC.

## Why this was worth testing

The accepted baseline already uses shared file-backed `mmap`, but on this workstation the runtime-data path originates from a Windows-host bind mount into Linux containers. Docker's own Windows/WSL guidance warns that bind-mounted files perform better when they come from the Linux filesystem, not the Windows filesystem.

That made this branch a valid test of:

1. shared index residency without per-request IPC
2. Linux-side volume locality instead of direct host bind-mount locality

## Full-stack result

Configuration:

- runtime-data: `runtime-data-256x128-radii-f32-stable-s524k`
- frontier: `8 / 128 / 48`
- `leafRadius = true`
- `lastTxPartition = false`
- CPU split: `0.15 / 0.425 / 0.425`

Observed result:

- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- `p99 = 1.38 ms`
- final score `5859.90`

That sits inside the existing accepted baseline band and does not beat the best observed exact stack run.

## Profile comparison

Fresh baseline profile:

| Scenario | API | Search p99 (us) | Total p99 (us) |
| --- | --- | ---: | ---: |
| baseline bind-mount | api1 | 786.16 | 817.18 |
| baseline bind-mount | api2 | 700.95 | 721.71 |
| shared data volume | api1 | 564.01 | 578.50 |
| shared data volume | api2 | 735.96 | 752.34 |

Interpretation:

- one replica improved materially
- the other replica regressed
- the end-to-end score run remained effectively unchanged

The signal was not stable enough to justify promotion.

## Decision

Keep this compose variant as a reusable experiment, but do not promote it as the default runtime topology.
