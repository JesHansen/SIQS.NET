# SIQS.NET — a quadratic sieve workbench

[![build](https://github.com/JesHansen/SIQS.NET/actions/workflows/build.yml/badge.svg)](https://github.com/JesHansen/SIQS.NET/actions/workflows/build.yml)

**Factor numbers. Understand how.**

Project site: **[siqs.net](https://siqs.net)**

SIQS.NET is an implementation of the **self-initializing quadratic sieve** in modern C# and .NET. It is both a factorization workbench and an explorable implementation of one of the classic general-purpose integer-factorization algorithms. Use it from the command line, drive it through a Blazor web UI, or distribute the sieving phase across other machines you control.

The project is designed to make the algorithm visible without making it toy-sized. A run passes through factor-base construction, polynomial sieving, filtering, linear algebra, and square-root recovery. Along the way, SIQS.NET records progress and artifacts that can be inspected in the UI.

> [!IMPORTANT]
> SIQS.NET is intended for education, experimentation, benchmarking, and factoring integers you are authorized to factor. It is not a replacement for a production cryptographic-audit program, and it should not be aimed at systems or keys without explicit permission.

## What you can do with it

- **Factor an integer locally** with the `qs` command-line application or the **Factorize** page.
- **Watch a live run**: phase status, elapsed time, progress, counters, result, and run artifacts are retained for inspection.
- **Spread sieving across machines**: the server coordinates work leases while volunteer clients rebuild and verify the same job parameters before sieving.
- **Download self-contained sieve clients** directly from the distributed UI — **Windows x64** and **Linux x64** are published by default, with **linux-arm64**, **osx-x64**, and **osx-arm64** a build flag away.
- **Compare parameter choices**: every tuning value the sieve has is a command-line option, documented in [docs/tuning.md](docs/tuning.md).
- **Learn the method** in **Sieve School**, which includes a guided tour, an animated sieve window, topic quizzes, and a historical timeline.
- **Work on individual pipeline stages** with focused command-line tools for factor-base generation, sieving, filtering, linear algebra, and square-root recovery.

## Quick start

### Prerequisites

- .NET 10 SDK
- PowerShell 7+, if using the repository build script (`build.ps1`) — it is cross-platform
- A modern browser for the web UI

Clone the repository and build once in Release:

```powershell
git clone https://github.com/JesHansen/SIQS.NET.git
cd SIQS.NET
dotnet build -c Release SIQS.slnx
```

That build produces a `qs` binary. Point it straight at the number you want factored — there's no need to `dotnet run` the project again for each factorization:

```powershell
.\QS\bin\Release\net10.0\qs.exe 48409030287755973455424746676949153939650071115833
```

A 50-digit semiprime takes a couple of seconds on a desktop machine, and every phase reports what it did. In an interactive terminal these appear as a live panel; redirected, they arrive as plain lines:

```text
── quadratic sieve ─────────────────────────────────────────────────────────────
  N = 48409030287755973455424746676949153939650071115833 (50 digits)

  ✔ factor base  2626 primes
  ✔ sieving  3307/3138 relations (full 2918, partial 2619)
  ✔ filtering  matrix 3307×2591 → 2054×2037
  ✔ linear algebra  63 null-space vectors
  ✔ square root  1 dependencies attempted, 1015 relations used

✔ factored
  48409030287755973455424746676949153939650071115833 =
  5944320017119142883155929 × 8143745651031914172071777

job J20260805-195827-0001 · 1.9s · artifacts: runs\J20260805-195827-0001
```

Small inputs are answered by trial division before the sieve ever starts, so pick something above
roughly 40 digits if you want to watch the algorithm work.

On Linux or macOS the same binary is `./QS/bin/Release/net10.0/qs`. The examples below are written
for PowerShell because that is where most of the development happens, but nothing in the solution
is Windows-only: CI builds and tests on both Linux and Windows, `build.ps1` runs under PowerShell 7
(`pwsh ./build.ps1`) on any platform, and `dotnet run --project ...` works everywhere.

The final argument is the integer to factor. Press `Ctrl+C` to cancel a running command-line job cleanly. Useful flags:

- `--quiet` prints only the factor product to stdout, for piping `qs` into a script or another tool.
- `--debug` restores the full per-phase counter dump.
- `--resume <run-dir>` resumes a canceled run from its saved artifacts (see `runs/` below) instead of starting over.
- `--parallelism 1` forces single-threaded execution, for byte-for-byte reproducible run artifacts.

`qs --help` prints every option, and [docs/tuning.md](docs/tuning.md) explains what each one does and
how its default is chosen. Nothing needs to be supplied: every parameter has a default derived from
the size of `N`, and the options exist so you can override one and compare.

`qs` also reads the target from stdin when no number is given, so it composes with other command-line tools, e.g. generating a random 55-digit semiprime with the included generator and factoring it in one line:

```powershell
.\CompositeGenerator\bin\Release\net10.0\CompositeGenerator.exe 55 | .\QS\bin\Release\net10.0\qs.exe
```

To use the web workbench instead, run it directly from source (no separate build step needed for iterating on it):

```powershell
dotnet run -c Release --project SIQS.UI/SIQS.UI.csproj --urls "http://localhost:5078"
```

Open `http://localhost:5078` in a browser. Binding to `0.0.0.0` instead of `localhost` makes the service reachable on the machine's network interfaces; use a firewall and a trusted network when doing that.

Verify the build with the test suite at any point:

```powershell
dotnet test --solution SIQS.slnx
```

## The web workbench

The web app is the easiest way to explore the complete system.

### Factorize

Enter a composite integer and start a one-shot factorization on the server. The advanced panel exposes tuning values such as the factor-base bound, multiplier, sieve interval, relation target, large-prime bound, and parallelism. Defaults are a good starting point; use the controls when comparing parameter choices or studying a particular run. The command line exposes the full set — see [docs/tuning.md](docs/tuning.md).

The live card displays the current pipeline phase. Completed runs show discovered factors, while failed or canceled jobs preserve their diagnostics.

### Jobs and artifacts

Every local run is recorded in the **Jobs** view. Open a job to see:

- the original target and job status;
- a phase timeline and elapsed time per phase;
- final factors or an error summary;
- counters and intermediate artifacts written during the run; and
- the parameters used for reproducibility.

Run data is stored under `runs/` in the application's content root. Keep that directory if you want to retain history between deployments, and back it up if the runs matter to you.

### Distributed

The **Distributed** page submits a job whose sieving phase can be shared by several machines. The server owns job coordination, leases chunks of polynomial work, validates uploaded relations, and performs filtering, linear algebra, and square-root recovery after enough relations arrive.

Workers do not blindly accept work. Each client performs a protocol handshake and independently reconstructs the factor base and sieving parameters; it declines the job if its reconstruction disagrees with the server.

> [!WARNING]
> There is no authentication on this page or on the worker API. Anyone who can reach the server can submit a job, lease work, and download an executable that other machines will then run. The page says so, and [SECURITY.md](SECURITY.md) sets out the threat model, what an operator can enforce with a reverse proxy, and which mitigations are known to be missing. Read it before binding to anything but `localhost`.

### Sieve School and history

The **Sieve School** and **History** sections together form an interactive companion to the implementation:

- a guided tour walks through a worked quadratic-sieve factorization;
- the sieve demo adds prime-log contributions step by step;
- topic quizzes cover foundations, factor bases, sieving, filtering, linear algebra, and square roots; and
- the history page connects Fermat, Kraitchik, Dixon, Pomerance, and RSA-129 to the algorithm in this repository.

## Joining a distributed sieve pool

Start the UI service on a reachable address, submit a distributed job in the browser, then download a client onto each worker. The clients are self-contained: a worker does not need the .NET runtime installed.

> [!IMPORTANT]
> **The downloadable clients are build output, not source.** A server started with `dotnet run` has never published them, so the download buttons and the `/api/dist/client/...` URLs will report that no client is available. Run `.\build.ps1` (or `dotnet publish SIQS.UI/SIQS.UI.csproj -c Release` and start the published app) first — that is what writes them into `download/` under the content root.
>
> A worker with a checkout of the repository does not need the download at all:
>
> ```powershell
> dotnet run -c Release --project QS.SieveClient -- http://siqs.example.net:5078
> ```

Replace `siqs.example.net:5078` below with the reachable address of your SIQS server.

### Windows x64

Download the client from the **Distributed** page, or retrieve it directly:

```powershell
Invoke-WebRequest http://siqs.example.net:5078/api/dist/client/windows-x64 -OutFile qs-sieve-client.exe
.\qs-sieve-client.exe http://siqs.example.net:5078
```

The default `/api/dist/client` URL serves the Windows client.

### Linux x64

On a 64-bit x86 Linux host, download and mark the file executable:

```bash
wget http://siqs.example.net:5078/api/dist/client/linux-x64 -O qs-sieve-client
chmod +x qs-sieve-client
./qs-sieve-client http://siqs.example.net:5078
```

Or, with `curl`:

```bash
curl -fLo qs-sieve-client http://siqs.example.net:5078/api/dist/client/linux-x64
chmod +x qs-sieve-client
./qs-sieve-client http://siqs.example.net:5078
```

The Linux client targets `linux-x64` (x86-64 Linux). It is a self-contained single-file .NET publish, but—as with most Linux binaries—it still expects a compatible Linux userspace.

### arm64 and macOS

The two x64 clients are published by default because they are what most volunteer machines run, and
because each extra runtime adds a full self-contained publish to the build. Nothing in the sieve is
x64-only — the AVX2 kernels have scalar fallbacks, which is exactly the path an arm64 or Apple
silicon machine takes — so the other targets build and run; they are simply not published unless
asked for:

```powershell
.\build.ps1 -Runtimes win-x64,linux-x64,linux-arm64,osx-arm64
```

Published clients appear on the **Distributed** page and at `/api/dist/client/<platform>` using the
slugs `windows-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`. A worker that has the
.NET SDK can skip the download entirely and run from a checkout:

```powershell
dotnet run -c Release --project QS.SieveClient -- http://siqs.example.net:5078
```

These targets are not covered by a performance sweep; expect the scalar fallback to be slower per
core than an AVX2 machine of comparable clock.

### Running workers responsibly

Each worker continually asks for a lease, sieves it locally, and uploads full and partial relations. Stop a worker with `Ctrl+C`; unfinished leases can be issued again by the server.

The distributed endpoints intentionally have a small, machine-oriented API:

| Endpoint | Purpose |
| --- | --- |
| `POST /api/dist/hello` | Client/server version and protocol handshake. |
| `GET /api/dist/job` | Fetch the active job descriptor. |
| `POST /api/dist/lease` | Request a sieving work lease. |
| `POST /api/dist/relations` | Upload verified relation data. |
| `GET /api/dist/status` | Read distributed-job status. |
| `GET /api/dist/client` | Download the Windows x64 worker (the default platform). |
| `GET /api/dist/client/{platform}` | Download a worker for `windows-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`. Returns 404 with an explanation when that client has not been published on this server. |

> [!CAUTION]
> The distributed-job submission and worker API are not an internet-facing multi-tenant service. If you expose SIQS beyond a trusted LAN, put it behind suitable authentication, TLS, firewalling, and rate limits. Treat a public server as an operational and security project of its own.

## Build, test, and publish

The repository-level build script is the supported release workflow:

```powershell
.\build.ps1
```

It performs the following work in order:

1. Cleans the solution's Release output.
2. Restores and builds the full solution in Release configuration.
3. Publishes self-contained, single-file distributed clients for `win-x64` and `linux-x64` (override with `-Runtimes`).
4. Runs the complete test suite.
5. Publishes the UI application, including the downloadable clients.

The published UI is placed at:

```text
SIQS.UI/bin/Release/net10.0/publish/
```

On Windows, run the published app with:

```powershell
.\SIQS.UI\bin\Release\net10.0\publish\SIQS.UI.exe --urls "http://0.0.0.0:5078"
```

The UI host is framework-dependent, so a machine running this package needs the .NET 10 runtime. The downloadable sieve workers are self-contained and do not.

The publish folder contains:

```text
publish/
├── SIQS.UI.exe
└── download/
    ├── windows-x64/
    │   └── qs-sieve-client.exe
    └── linux-x64/
        └── qs-sieve-client
```

To skip the test run while iterating on a release package:

```powershell
.\build.ps1 -SkipTests
```

You can also publish the UI project directly. Its publish target prepares both worker binaries before copying them into the deployment package:

```powershell
dotnet publish SIQS.UI/SIQS.UI.csproj -c Release
```

## Development commands

Run these from the repository root:

```powershell
dotnet restore SIQS.slnx
dotnet build SIQS.slnx
dotnet test --solution SIQS.slnx
dotnet run --project SIQS.UI/SIQS.UI.csproj --urls "http://localhost:5078"
dotnet run --project QS/QS.csproj -- 48409030287755973455424746676949153939650071115833
```

Without `--urls`, the UI binds the ports in `SIQS.UI/Properties/launchSettings.json` (5216 for HTTP,
7248 for HTTPS) and prints them at startup; pass `--urls` when you want a fixed address.

The build treats warnings as errors. That is deliberate: a warning-free build is part of the project's definition of a healthy change.

For a tighter development loop, run an individual test project:

```powershell
dotnet test --project Factorbase.Tests/Factorbase.Tests.csproj
dotnet test --project Sieving.Tests/Sieving.Tests.csproj
dotnet test --project SIQS.UI.Tests/SIQS.UI.Tests.csproj
```

## How SIQS works

Factoring asks for non-trivial integers `p` and `q` such that:

```text
N = p × q
```

The quadratic sieve looks for a congruence of squares:

```text
X² ≡ Y² (mod N)
```

When `X` is not congruent to `±Y`, the greatest common divisor of `X - Y` and `N` often exposes a factor. The difficult part is constructing such a pair of squares efficiently.

SIQS does so through a pipeline:

1. **Factor base** — choose primes for which the target has suitable quadratic-residue behavior.
2. **Polynomial generation and sieving** — evaluate many values near a square root and add logarithmic weights where factor-base primes divide them.
3. **Relation collection** — retain values that factor over the base, including useful large-prime partial relations.
4. **Filtering** — remove unhelpful rows and combine compatible partials to shrink the problem.
5. **Linear algebra over GF(2)** — find a dependency in the parity vectors of prime exponents.
6. **Square-root recovery** — turn that dependency into a congruence of squares and compute gcds for factors.

The “self-initializing” part refers to using multiple carefully constructed polynomials so sieving can continue efficiently without manually retuning each polynomial.

## Repository tour

The solution is deliberately divided by algorithmic responsibility.

| Path | Responsibility |
| --- | --- |
| `Factorbase/` | Factor-base construction and related number-theory setup. |
| `Sieving/` | Polynomial generation, sieving, and raw relation collection. |
| `Filtering/` | Relation cleanup, partial-relation handling, and matrix reduction. |
| `LinearAlgebra/` | Parity-vector solving over GF(2). |
| `SquareRoot/` | Dependency reconstruction and final gcd extraction. |
| `SIQS.Pipeline/` | End-to-end orchestration of a local factorization. |
| `SIQS.Contracts/` | Shared records, run data, protocol types, and contracts. |
| `SIQS.Overlord/` | Distributed job coordination, leases, and relation intake. |
| `SIQS.UI/` | Blazor workbench, HTTP endpoints, history, and learning tools. |
| `QS.SieveClient/` | Cross-platform distributed sieving worker. |
| `QS/` | Main command-line factorization tool. |
| `QS-FB/`, `QS.Sieve/`, `QS-Filter/`, `QS-LinAlg/`, `QS.Sqrt/` | Focused command-line entry points for pipeline stages. |
| `CompositeGenerator/` | Generates random semiprimes of a requested decimal size, for feeding `qs`. |
| `SIQS.Benchmarks/` | BenchmarkDotNet suites and measurement tools for individual kernels. |
| `SIQS.PerformanceSpy/` | End-to-end timing sweep across digit sizes, for catching regressions. |
| `docs/` | Reference documentation, starting with the [tuning reference](docs/tuning.md). |
| `*.Tests/` | Unit and integration tests for the corresponding modules. |

The focused stage tools are useful when studying a saved artifact, isolating a performance question, or experimenting with one part of the pipeline without running a complete factorization.

## Data, reproducibility, and operations

Each job has an identifier and a run directory. The job view surfaces the inputs, phase state, elapsed times, counters, result, and available files. This makes it practical to compare parameter choices and revisit a run after it finishes.

For long-running or distributed use:

- keep `runs/` on durable storage;
- reserve enough disk space for relation and job artifacts;
- ensure all workers can reach the server URL they were given;
- use a stable server address rather than `localhost` for remote workers; and
- protect the server if it is reachable by anything other than machines you trust.

## Contributing

Contributions are welcome: correctness improvements, better diagnostics, performance measurements, tests, UI polish, documentation, and new learning material all help. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to get started, coding style, and pull request expectations.

Participation is governed by the [Contributor Covenant](CODE_OF_CONDUCT.md). To report a vulnerability, see [SECURITY.md](SECURITY.md).

## License

The code, documentation, and build tooling are released under the [MIT License](LICENSE).

The MIT License does **not** cover every file in the tree: some of the portraits under `SIQS.UI/wwwroot/img/history/` are third-party works under their own terms, including one copyrighted press photograph and one copyleft (CC BY-SA 2.0 FR) image. [NOTICE.md](NOTICE.md) lists them and explains what they mean for a fork or a redistribution.

## Acknowledgements

The quadratic sieve belongs to a long line of ideas: Fermat's difference of squares, Kraitchik's congruences, Dixon's random squares, and Carl Pomerance's quadratic sieve. SIQS exists to make that lineage runnable, inspectable, and fun to explore.

### Standing on the shoulders of msieve and YAFU

SIQS.NET owes an enormous debt to [**msieve**](https://github.com/radii/msieve), Jason Papadopoulos's remarkable factoring library, and [**YAFU — Yet Another Factoring Utility**](https://github.com/bbuhrow/yafu), maintained by Ben Buhrow and its contributors. Their source code, engineering decisions, documentation, and hard-won practical knowledge of integer factorization were indispensable inspiration for this project.

These are extraordinary pieces of numerical software. They demonstrate what careful mathematical implementation, relentless performance work, and years of real-world factorization experience can achieve. Anyone interested in practical quadratic-sieve or general integer-factorization software should study them.

Quite simply: SIQS.NET would not exist without msieve and YAFU. Thank you to their authors and contributors for making that work available to the community.

Happy sieving.
