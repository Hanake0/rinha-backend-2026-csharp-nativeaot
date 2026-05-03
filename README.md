# Rinha de Backend 2026 - C# NativeAOT

Submission workspace for a `C#` / `.NET 10` / `NativeAOT` entry for the 2026 Rinha de Backend.

Current intent:

- keep the runtime minimal;
- optimize for `p99 <= 1ms`, with `sub-0.5ms` as internal headroom rather than as a scoring target;
- treat the index/search path as the main problem, not the HTTP framework.

Planning documents live outside this repo in [`../plans`](../plans/).
