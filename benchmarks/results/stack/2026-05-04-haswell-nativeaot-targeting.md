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

There are two confidence levels we should keep separate.

### Explicitly confirmed by vendor pages

Apple confirms the machine/SKU class only.

Intel ARK for this CPU family explicitly shows these extension families on the public page:

- `SSE4.1`
- `SSE4.2`
- `AVX2`
- `AES-NI`

### Exact Linux flag dump from the target machine

The organizer-provided machine dump for the target CPU confirms this exact model and these flags:

- `model name = Intel(R) Core(TM) i5-4278U CPU @ 2.60GHz`
- `family = 6`
- `model = 69`
- `stepping = 1`

Relevant flags for our workload:

- `mmx`
- `sse`
- `sse2`
- `pni` (`sse3`)
- `ssse3`
- `sse4_1`
- `sse4_2`
- `pclmulqdq`
- `aes`
- `avx`
- `avx2`
- `fma`
- `f16c`
- `movbe`
- `popcnt`
- `abm` (`lzcnt` class behavior)
- `bmi1`
- `bmi2`
- `rdrand`
- `fsgsbase`
- `erms`
- `invpcid`
- `xsaveopt`

Important nuance:

- Intel ARK's "Instruction Set Extensions" field is abbreviated and does not enumerate every CPUID feature the compiler may care about
- it is useful as a confirmation source, but not a complete feature dump

### Strongly expected for a Haswell-class target / `x86-64-v3`

`x86-64-v3` is the standard x64 CPU profile that maps to AVX2-era processors such as Haswell-class Intel CPUs.

Features associated with that profile include:

- `AVX`
- `AVX2`
- `F16C`
- `FMA`
- `BMI1`
- `BMI2`
- `LZCNT`
- `MOVBE`
- `POPCNT`

These are not all explicitly enumerated on the public ARK summary, but they are the reason `x86-64-v3` is the practical compiler target for this machine class.

### Exact `x86-64-v3` delta versus the target dump

`x86-64-v3` covers the important compute-side part of the target machine:

- `sse4_1`
- `sse4_2`
- `avx`
- `avx2`
- `f16c`
- `fma`
- `bmi1`
- `bmi2`
- `lzcnt`
- `movbe`
- `popcnt`

Useful target-CPU flags that are outside the `x86-64-v3` baseline:

- `aes`
- `pclmulqdq`
- `rdrand`
- `fsgsbase`
- `erms`
- `invpcid`
- `xsaveopt`

For our current NativeAOT build, the only safe and relevant extra `ilc` switch we explicitly layer on top today is:

- `aes`

Notes:

- current `ilc` clearly supports `aes`
- current `ilc` does not clearly expose a standalone `pclmulqdq` token in its help output, even though older docs/examples mention `pclmul`
- several other flags in the CPU dump are platform/runtime features rather than useful app-code generation knobs for this workload

### Why this distinction matters

If we wanted absolute proof of every CPUID bit, we would need a real feature dump from the organizer box itself, such as `lscpu` or `/proc/cpuinfo`.

For tuning decisions today, the practical rule is:

- use Intel/Apple pages for hard confirmation of the machine and major SIMD families
- use `x86-64-v3` as the safest useful `ilc` profile for a Haswell-class AVX2 machine

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
- `RinhaIlcInstructionSet=x86-64-v3,aes`
- `RinhaIlcMaxVectorTBitWidth=256`

This does two useful things:

1. It enables a Haswell-class x64 target profile instead of the generic x64 baseline.
2. It lets `Vector<T>` grow to 256-bit where the compiler/runtime can use it.

## Local result after targeting Haswell

Using the local official-style compose benchmark with the stable dataset and official `test.js` / `test-data.json`:

- `run 1 p99 = 0.67 ms`
- `run 2 p99 = 0.67 ms`
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

### 2. F16 path still does not use a direct F16C conversion intrinsic

File:

- `src/Rinha2026.Core/Search/DistanceComputations.cs:117-151`

Current state:

- `SquaredL2F16Fixed16()` still expands every half value through the lookup table
- we now at least use AVX/FMA for the accumulation step after decoding

Opportunity:

- if/when the relevant `F16C` conversion intrinsic is cleanly available in our .NET surface, replace lookup-table decode with direct packed half-to-float conversion
- keep the current AVX/FMA accumulation path either way

### 3. Q8 path is vectorized only for one exact shape

File:

- `src/Rinha2026.Core/Search/DistanceComputations.cs:80-115`

Current state:

- the AVX2 path only runs for 16-dimension vectors
- reduction copies lanes to stack memory before summing

Opportunity:

- add a generic AVX2 bulk path with tail handling
- reduce register-to-scalar with fewer spills

### 4. AES is confirmed but irrelevant to the hot path today

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
