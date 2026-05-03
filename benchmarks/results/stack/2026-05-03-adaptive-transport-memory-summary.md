# 2026-05-03 Adaptive, Transport, and Memory Summary

## Scope

This file records the follow-up exploration pass after the first exact corrected-stable baseline. The purpose of this pass was:

1. test an adaptive rerank expansion path that spends more work only on decision-boundary cases
2. isolate transport and load-balancer overhead instead of blaming search blindly
3. verify whether the current runtime is disk-bound or already mostly memory-resident

## Exact stable baseline used for comparison

- runtime-data root: `runtime-data-256x128-radii-f32-stable-s524k`
- frontier: `beamLevel1 = 8`, `beamLevel2 = 128`, `rerankCount = 48`
- detection: `topK = 5`, `approvalThreshold = 0.6`
- pruning:
  - `useLeafRadiusPruning = true`
  - `useLastTransactionPartitionPruning = false`
- constrained stack anchor:
  - `p99 = 1.36 ms`
  - `FP = 0`
  - `FN = 0`
  - `http_errors = 0`
  - final score `5865.04`

## Adaptive boundary rerank experiment

The exact baseline spends `48` full-precision rerank distance computations on every request. This branch tested whether we could:

- rerank only `32` candidates in the common path
- expand to `48` only when the provisional fraud count was on the approval boundary

### Evaluator findings

Stable findings on the official evaluator:

- `32 -> 32`: `FP = 0`, `FN = 1`
- `32 -> 36`: `FP = 0`, `FN = 1`
- `32 -> 40`: `FP = 0`, `FN = 1`
- `32 -> 44`: `FP = 0`, `FN = 1`
- `32 -> 48`: `FP = 0`, `FN = 0`
- `40 -> 48`: `FP = 0`, `FN = 0`

The single known `32 -> 32` miss was a boundary case whose exact answer depended on promoting one more fraud hit into the top-`5`.

### Compose finding

The adaptive `32 -> 48` branch recovered exactness, but the full constrained stack still regressed:

- `p99 = 1.41 ms`
- `FP = 0`
- `FN = 0`
- `http_errors = 0`
- final score `5850.77`

Conclusion:

- correctness recovered
- service-only search savings did not survive the real stack
- keep as an experiment, do not promote

## Transport findings

### Nginx vs direct dual-target

Earlier direct dual-target replay landed near `1.28 ms`. The equivalent nginx-backed compose stack landed near `1.36 ms`.

Inference:

- load balancing plus extra socket hops currently account for roughly `~0.08 ms` of the end-to-end gap
- that is meaningful, but it is not large enough by itself to explain the whole `1.36 ms -> 1.00 ms` delta

### Unix domain socket path

The stack was also tested with nginx proxying over unix sockets to the APIs.

Observed outcome:

- worse than the tcp nginx baseline on this machine
- not promoted

### Kestrel `IOQueueCount`

A short constrained transport comparison was run with `Runtime:Http:IoQueueCount = 0`.

Observed outcome:

- slightly worse than the short exact baseline
- not promoted

Conclusion:

- transport still matters
- the fastest path is not yet “obviously unix sockets”
- search cost is still the dominant in-service stage

## Memory findings

### How the index is loaded today

The current runtime is already file-backed and read-only:

- `vectors.q8.bin`: memory-mapped
- `vectors.f16.bin`: memory-mapped
- `vectors.f32.bin`: memory-mapped
- postings and metadata: memory-mapped

Both API containers mount the same host `runtime-data` directory read-only:

```yaml
volumes:
  - ${RUNTIME_DATA_DIR:-./runtime-data}:/app/data:ro
```

This means the baseline is not doing per-request disk parsing or ad hoc file IO. The question is whether we should:

1. warm more of those mapped pages eagerly
2. promote small hot structures into process memory
3. avoid promoting large corpora that would just blow per-container memory

### Artifact sizes on the accepted dataset

- `vectors.q8.bin`: `45.78 MiB`
- `vectors.f16.bin`: `91.55 MiB`
- `vectors.f32.bin`: `183.11 MiB`
- `leaf.postings.ids.bin`: `11.44 MiB`
- `vectors.original.ids.bin`: `11.44 MiB`
- `leaf.centroids.f32.bin`: `2.00 MiB`
- other metadata files: small

### Observed memory under load

- `api1`: `~17.2 MiB / 151 MiB`
- `api2`: `~17.2 MiB / 151 MiB`
- `lb`: `~4.9 MiB / 48 MiB`

Conclusion:

- there is real headroom for more anonymous memory
- copying the full `f32` corpus into both API containers is still the wrong shape
- the justified next branch is selective promotion:
  - centroids
  - postings offsets
  - label bitset
  - stable-order ids
  - possibly posting ids and q8 vectors
- plus optional startup page warming for the large mapped vector files

## Current conclusion

The current exact baseline is close enough that the next branches should stay disciplined:

1. preserve the exact stable baseline
2. test memory-promotion knobs that can plausibly reduce search tail without duplicating the full corpus
3. benchmark a purpose-built load balancer only after the memory branch is measured
4. keep exact-search rewrites on the table if the above stalls
