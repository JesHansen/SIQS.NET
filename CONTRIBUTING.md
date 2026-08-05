# Contributing to SIQS.NET

Contributions are welcome: correctness improvements, better diagnostics, performance measurements, tests, UI polish, documentation, and new learning material all help.

## Getting started

```powershell
dotnet restore SIQS.slnx
dotnet build SIQS.slnx
dotnet test --solution SIQS.slnx
```

`dotnet build` treats warnings as errors, so keep new code warning-free. While iterating, it's usually faster to build and test a single project, e.g.:

```powershell
dotnet test --project Factorbase.Tests/Factorbase.Tests.csproj
```

(The suite runs on Microsoft Testing Platform, so `dotnet test` needs the explicit `--solution` /
`--project` switch rather than a bare path.)

Examples in this repository are written for PowerShell, but the solution is not Windows-only — CI
builds and tests on both Linux and Windows, and `build.ps1` runs under cross-platform PowerShell 7
(`pwsh ./build.ps1`). On Linux and macOS the built tools have no `.exe` suffix, e.g.
`./QS/bin/Release/net10.0/qs`.

## Project layout

This is a .NET 10 SIQS (Self-Initializing Quadratic Sieve) solution. Core libraries are organized by pipeline stage: `Factorbase/`, `Sieving/`, `Filtering/`, `LinearAlgebra/`, `SquareRoot/`, and shared contracts in `SIQS.Contracts/`. `SIQS.Pipeline/` coordinates end-to-end runs. Matching `*.Tests/` projects contain unit and integration tests. Command-line entry points are in `QS/`, `QS-FB/`, `QS-Filter/`, `QS-LinAlg/`, `QS.Sieve/`, and `QS.Sqrt/`; `SIQS.UI/` contains a Blazor web interface. The solution is `SIQS.slnx`.

To run the applications from source while working on them:

```powershell
dotnet run --project SIQS.UI/SIQS.UI.csproj
dotnet run --project QS/QS.csproj -- <arguments>
```

## Coding style and naming conventions

An [`.editorconfig`](.editorconfig) encodes the conventions below, so most editors will apply them automatically.

Use four-space indentation, file-scoped namespaces where consistent with the surrounding file, and concise idiomatic C#. Nullable reference types and implicit usings are enabled globally; keep new code warning-free. Use `PascalCase` for types, methods, and public members, `camelCase` for locals and parameters, `_camelCase` for private fields, and `I`-prefixed names for interfaces. Keep algorithm-specific code in its corresponding module and preserve existing public contracts.

C# can be written in a variety of ways. This project's approach is as follows:

- Performance is king. If a certain step requires a certain code style to be maximally performant, use that. But don't pick a programming style that's just C in disguise without having benchmarked that it is actually required. Never guess that some style may be too slow. Always measure.
- Aim to use modern, idiomatic C#, which means a heavy lean towards a functional programming style: LINQ for data transformation and immutable records for data.
- Remember still that C# is mostly an object orientated language. Use lots of classes that have small areas of responsibility. Compose classes for more high level abstractions. Classes are small, they encapsulate between one and five things. Their public api is intentionally kept small: do few things well. They have only a handful screens worth of code each.
- Eschew primitive obsession. Instead of, for example, raw int arrays use record wrappers to make them named types. Use extension methods to give these wrappers functionality.

## Algorithmic and performance changes

Algorithmic changes deserve a little extra care:

- A faster-looking implementation is only an improvement when it's actually measured to be faster — don't guess that a style is required for performance, benchmark it.
- Keep the mathematical invariants and serialized run/relation file formats intact, or call out the break explicitly.
- Preserve existing public contracts (`SIQS.Contracts`) where possible; downstream pipeline stages and tests depend on them.

Several plausible-sounding optimizations have already been built and measured *negative*. Where that
happened, the comment beside the current implementation records it — grep the stage you're about to
change before investing in an idea.

### How to measure

Micro-benchmarks live in `SIQS.Benchmarks/` and run on BenchmarkDotNet. Always in Release:

```powershell
dotnet run -c Release --project SIQS.Benchmarks -- --list flat
dotnet run -c Release --project SIQS.Benchmarks -- --filter *SieveBenchmarks*
dotnet run -c Release --project SIQS.Benchmarks -- --filter *BlockLanczosMatrixBenchmarks*
```

The same project hosts measurement tools for questions a benchmark class doesn't answer well, such
as `--capture-residuals`, `--benchmark-residuals`, `--replay-cofactor`, `--screen-share`,
`--candidate-scaling`, and `--compare-spv`.

For end-to-end effects — where a kernel win can be eaten by relation quality — sweep whole
factorizations across digit sizes:

```powershell
dotnet run -c Release --project SIQS.PerformanceSpy -- --help
```

When reporting numbers, include the target size, before/after wall and CPU time, the number of
repetitions, and the CPU model. The sieve has AVX2 kernels with scalar fallbacks, so results are not
comparable across machines.

## Pull requests

- Keep changes focused; unrelated cleanup makes a PR harder to review.
- Add or update tests for behavior changes.
- Describe what changed and why in the PR description, especially for anything performance- or format-sensitive.

CI builds and tests every pull request in Release configuration.

## Code of conduct

Participation in this project is governed by the [Contributor Covenant](CODE_OF_CONDUCT.md).

## Reporting security issues

Please don't open a public issue for a suspected vulnerability — see [SECURITY.md](SECURITY.md) for how to report it privately.
