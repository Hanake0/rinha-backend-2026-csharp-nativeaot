# Working Rules

This repo is being built under contest-style constraints.

## Formatting

- follow [`.editorconfig`](./.editorconfig) for all generated and handwritten code;
- treat style drift as a defect, not as cleanup for later;
- prefer explicit types in C# unless a local exception is clearly better.

## Commits

- one logical task per commit;
- keep commits small enough to review in isolation;
- use `gitmoji + conventional commits` style.

Expected shape:

```text
<gitmoji> <type>(<scope>): <summary>
```

Examples:

```text
✨ feat(parser): add manual merchant id parser
⚡ perf(search): vectorize q8 posting scan
📝 docs(benchmarks): add constrained compose benchmark policy
🐛 fix(api): handle null last_transaction sentinel path
```

## Benchmark policy

- no performance claim without a benchmark;
- micro-benchmarks are necessary but not sufficient;
- stack-level decisions should be validated under constrained Docker Compose.

Preferred benchmark order:

1. micro benchmark
2. component benchmark
3. service benchmark
4. constrained compose stack benchmark

## Constraint discipline

- optimize for `0.5 ms` from day one;
- keep the runtime topology compliant with `1 lb + 2 api` services;
- avoid current-participant code inspection or "inspiration";
- prefer maintainable high-performance code before resorting to opaque tricks.

## Plan documents

The working plans currently live outside this repo in:

- `../plans`

If we want the planning artifacts versioned with the same commit discipline, we should either move them into this repo or initialize `../plans` as its own git repo.
