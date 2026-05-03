# 2026-05-03 Class-Aware Exact Fraud-Count Search

## Goal

Reduce exact-search tail latency by exploiting the fact that the final decision depends on the fraud count inside the exact `topK`, not on a full mixed-class ranking by itself.

For the current contest configuration:

- `topK = 5`
- `approvalThreshold = 0.6`
- denial requires at least `3` fraud neighbors

That allowed a branch that kept separate fraud and legit frontiers, reranked them independently, then merged them into the final exact `topK`.

## Result

The branch stayed exact, but it was slower across the full rerank sweep.

| Rerank | FP | FN | Search p99 (us) | Search mean (us) |
| --- | --- | --- | ---: | ---: |
| 10 | 0 | 0 | 1589.1 | 493.4 |
| 12 | 0 | 0 | 1668.7 | 505.9 |
| 16 | 0 | 0 | 1598.6 | 490.1 |
| 20 | 0 | 0 | 1531.5 | 492.6 |
| 24 | 0 | 0 | 1592.1 | 498.5 |
| 32 | 0 | 0 | 1565.0 | 492.1 |
| 40 | 0 | 0 | 1555.3 | 488.6 |
| 48 | 0 | 0 | 1570.0 | 496.8 |

Reference accepted exact baseline on the same artifact family:

- evaluator search `p99 = 510.6 us`
- compose stack `p99 = 1.35-1.43 ms`

## Interpretation

- The formulation is valid and exact.
- It also proves that the current artifact family contains enough local class signal to remain exact even at very low rerank counts in this alternate path.
- That did not translate into a faster implementation because the extra per-candidate class bookkeeping and dual-frontier maintenance cost more than the saved rerank work.

## Decision

Reject this branch.

The next work should focus on either:

1. reducing stack-visible HTTP overhead, or
2. using spare RAM for selective hot-structure promotion that directly shrinks the dominant search stage without duplicating the full index per API.
