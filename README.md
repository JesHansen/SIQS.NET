# SIQS.NET — a quadratic sieve workbench

**Factor numbers. Understand how.**

SIQS.NET is an implementation of the **self-initializing quadratic sieve** in modern C# and .NET. It is both a factorization workbench and an explorable implementation of one of the classic general-purpose integer-factorization algorithms. Use it from the command line, drive it through a Blazor web UI, or distribute the sieving phase across other machines you control.

The project is designed to make the algorithm visible without making it toy-sized. A run passes through factor-base construction, polynomial sieving, filtering, linear algebra, and square-root recovery. Along the way, SIQS.NET records progress and artifacts that can be inspected in the UI.

> [!IMPORTANT]
> SIQS.NET is intended for education, experimentation, benchmarking, and factoring integers you are authorized to factor. It is not a replacement for a production cryptographic-audit program, and it should not be aimed at systems or keys without explicit permission.

## What you can do with it

- **Factor an integer locally** with the `qs` command-line application or the **Factorize** page.
- **Watch a live run**: phase status, elapsed time, progress, counters, result, and run artifacts are retained for inspection.
- **Spread sieving across machines**: the server coordinates work leases while volunteer clients rebuild and verify the same job parameters before sieving.
- **Download self-contained sieve clients** directly from the distributed UI for **Windows x64** and **Linux x64**.
- **Learn the method** in **Sieve School**, which includes a guided tour, an animated sieve window, topic quizzes, and a historical timeline.
- **Work on individual pipeline stages** with focused command-line tools for factor-base generation, sieving, filtering, linear algebra, and square-root recovery.

## Quick start

### Prerequisites

- .NET 10 SDK
- PowerShell, if using the repository build script on Windows
- A modern browser for the web UI

Clone the repository, restore packages, and start the UI:

```powershell
dotnet restore SIQS.slnx
dotnet run --project SIQS.UI/SIQS.UI.csproj --urls "http://0.0.0.0:5078"
```

Open `http://localhost:5078` in a browser. Binding to `0.0.0.0` makes the service reachable on the machine's network interfaces; use a firewall and a trusted network when doing that.

To run a small local factorization from the command line:

```powershell
dotnet run --project QS/QS.csproj -- 15347
```

The final argument is the integer to factor. Press `Ctrl+C` to cancel a running command-line job cleanly.

## The web workbench

The web app is the easiest way to explore the complete system.

### Factorize

Enter a composite integer and start a one-shot factorization on the server. The advanced panel exposes tuning values such as the factor-base bound, multiplier, sieve interval, relation target, large-prime bound, and parallelism. Defaults are a good starting point; use the controls when comparing parameter choices or studying a particular run.

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

The **Distributed** page lets an administrator submit a job whose sieving phase can be shared by several machines. The server owns job coordination, leases chunks of polynomial work, validates uploaded relations, and performs filtering, linear algebra, and square-root recovery after enough relations arrive.

Workers do not blindly accept work. Each client performs a protocol handshake and independently reconstructs the factor base and sieving parameters; it declines the job if its reconstruction disagrees with the server.

### Sieve School and history

The **Sieve School** and **History** sections together form an interactive companion to the implementation:

- a guided tour walks through a worked quadratic-sieve factorization;
- the sieve demo adds prime-log contributions step by step;
- topic quizzes cover foundations, factor bases, sieving, filtering, linear algebra, and square roots; and
- the history page connects Fermat, Kraitchik, Dixon, Pomerance, and RSA-129 to the algorithm in this repository.

## Joining a distributed sieve pool

Start the UI service on a reachable address, submit a distributed job in the browser, then download a client onto each worker. The clients are self-contained: a worker does not need the .NET runtime installed.

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
| `GET /api/dist/client/windows-x64` | Download the Windows x64 worker. |
| `GET /api/dist/client/linux-x64` | Download the Linux x64 worker. |

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
3. Publishes self-contained, single-file distributed clients for `win-x64` and `linux-x64`.
4. Runs the complete test suite.
5. Publishes the UI application, including both downloadable clients.

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
dotnet test SIQS.slnx
dotnet run --project SIQS.UI/SIQS.UI.csproj
dotnet run --project QS/QS.csproj -- 15347
```

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

Contributions are welcome: correctness improvements, better diagnostics, performance measurements, tests, UI polish, documentation, and new learning material all help.

Before opening a change, build and test the solution:

```powershell
dotnet build SIQS.slnx
dotnet test SIQS.slnx
```

Algorithmic changes deserve a little extra care. A faster-looking implementation is only an improvement when it is measured and keeps the mathematical invariants and serialized run formats intact.

## Acknowledgements

The quadratic sieve belongs to a long line of ideas: Fermat's difference of squares, Kraitchik's congruences, Dixon's random squares, and Carl Pomerance's quadratic sieve. SIQS exists to make that lineage runnable, inspectable, and fun to explore.

### Standing on the shoulders of msieve and YAFU

SIQS.NET owes an enormous debt to [**msieve**](https://github.com/radii/msieve), Jason Papadopoulos's remarkable factoring library, and [**YAFU — Yet Another Factoring Utility**](https://github.com/bbuhrow/yafu), maintained by Ben Buhrow and its contributors. Their source code, engineering decisions, documentation, and hard-won practical knowledge of integer factorization were indispensable inspiration for this project.

These are extraordinary pieces of numerical software. They demonstrate what careful mathematical implementation, relentless performance work, and years of real-world factorization experience can achieve. Anyone interested in practical quadratic-sieve or general integer-factorization software should study them.

Quite simply: SIQS.NET would not exist without msieve and YAFU. Thank you to their authors and contributors for making that work available to the community.

Happy sieving.
