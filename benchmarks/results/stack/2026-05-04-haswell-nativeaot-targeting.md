# 2026-05-04 Haswell NativeAOT targeting

## Why this note exists

The official Rinha 2026 hardware is described as a Mac mini (Late 2014) with a 2.6 GHz CPU and 8 GB RAM running Ubuntu 24.04.

Apple's official page confirms the machine class and the 2.6 GHz dual-core Intel Core i5 SKU option:

- https://support.apple.com/en-us/111931

EveryMac maps that exact 2.6 GHz Late 2014 Mac mini configuration to:

- Intel Core i5-4278U
- Haswell ULT
- 2 cores / 4 threads
- Turbo up to 3.1 GHz

Source:

- https://everymac.com/systems/apple/mac_mini/specs/mac-mini-core-i5-2.6-late-2014-specs.html

## NativeAOT mapping

The local `ilc` help for .NET 10 exposes predefined CPU instruction-set groups, including:

- `x86-64`
- `x86-64-v2`
- `x86-64-v3`
- `x86-64-v4`

For this Haswell-class CPU, `x86-64-v3` is the best direct match.

Important detail:

- the current `ilc` accepts `bmi` in examples, but `bmi2` is not accepted as a standalone token in this tool build
- using the predefined `x86-64-v3` profile is safer than trying to hand-assemble the exact feature list through individual tokens

## Practical ISA picture for the target box

For a Late 2014 2.6 GHz Haswell ULT i5-4278U, the practically relevant x64 features are:

- `SSE4.1`
- `SSE4.2`
- `SSSE3`
- `POPCNT`
- `AVX`
- `AVX2`
- `F16C`
- `FMA`
- `BMI1`
- `BMI2`
- `LZCNT`
- `MOVBE`

Common Haswell crypto extensions that are also expected on this SKU:

- `AES-NI`
- `PCLMULQDQ`

Features we should not target for this machine:

- `AVX-512`
- `AVX10`
- `VNNI`
- `GFNI`
- Intel `SHA`
- `APX`

## What NativeAOT was doing before

NativeAOT defaults to the minimum instruction set supported by the target OS and architecture.

That means the untuned `linux-musl-x64` build was conservative and not specifically aligned to Haswell.

Effects of the untuned baseline:

- explicit `Avx2.IsSupported` fast paths are not a safe assumption for code generation
- `Vector<T>` width is not guaranteed to widen to 256-bit behavior
- codegen is not biased toward Haswell-era throughput

## What we changed

The API Dockerfiles now set:

- `RinhaOptimizationPreference=Speed`
- `RinhaIlcInstructionSet=x86-64-v3`
- `RinhaIlcMaxVectorTBitWidth=256`

This does two useful things:

1. It enables a Haswell-class x64 target profile instead of the generic x64 baseline.
2. It lets `Vector<T>` grow to 256-bit where the compiler/runtime can use it.

## Local result after targeting Haswell

Using the local official-style compose benchmark with the stable dataset and official `test.js` / `test-data.json`:

- `p99 = 0.79 ms`
- `false positives = 0`
- `false negatives = 0`
- `http errors = 0`
- `final score = 6000`

This is the first strong signal that the generic AOT baseline was leaving performance on the table.

## What we are already using effectively

Current code already contains one explicit AVX2 path:

- `src/Rinha2026.Core/Search/DistanceComputations.cs`
- `SquaredL2Q8()` switches to `SquaredL2Q8Vectorized()` when `Avx2.IsSupported` and vector length is 16

That path uses:

- `Avx2.ConvertToVector256Int16`
- `Avx2.Subtract`
- `Avx2.MultiplyAddAdjacent`

This path benefits directly from the Haswell-class target.

## What we are not using well yet

These are the highest-confidence remaining ISA-aware opportunities in the current codebase.

### 1. F32 distance still relies on generic vectors or scalar unrolling

Files:

- `src/Rinha2026.Core/Search/DistanceComputations.cs:11-38`
- `src/Rinha2026.Core/Search/DistanceComputations.cs:154-188`

Current state:

- `SquaredL2()` uses `Vector<float>` rather than explicit AVX/AVX2/FMA intrinsics
- `SquaredL2F32Fixed16()` is fully scalar and manually unrolled

Opportunity:

- add explicit `Avx` / `Fma` kernels for the fixed 16-float case
- use 256-bit loads and fused multiply-add accumulation

### 2. F16 path does not exploit F16C

File:

- `src/Rinha2026.Core/Search/DistanceComputations.cs:117-151`

Current state:

- `SquaredL2F16Fixed16()` expands every half value through the lookup table and does scalar math

Opportunity:

- add an `F16C` conversion path for fixed-width half vectors
- convert packed half values to floats in registers and then use `Avx` / `Fma`

### 3. Q8 path is vectorized only for one exact shape

File:

- `src/Rinha2026.Core/Search/DistanceComputations.cs:80-115`

Current state:

- the AVX2 path only runs for 16-dimension vectors
- reduction copies lanes to stack memory before summing

Opportunity:

- add a generic AVX2 bulk path with tail handling
- reduce register-to-scalar with fewer spills

### 4. AES is likely available but irrelevant to the hot path today

Current state:

- no obvious hot-path crypto workloads were found in the scoring path

Conclusion:

- `AES-NI` availability is nice to know, but it is not currently where the scoring win is coming from

## Recommended next SIMD experiments

In order:

1. AVX/FMA implementation for `SquaredL2F32Fixed16()`
2. F16C + AVX/FMA implementation for `SquaredL2F16Fixed16()`
3. Broaden the AVX2 Q8 path beyond the exact 16-dimension case
4. Re-benchmark old SIMD ideas only after rebuilding with the Haswell AOT target

## Bottom line

Yes, the Haswell-specific AOT target is justified.

The best `ilc` profile match for the official 2.6 GHz Late 2014 Mac mini is `x86-64-v3`, and our local official-style benchmark improved to a `6000` result once we actually built for that class of CPU.
