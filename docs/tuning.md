# Tuning reference

Every parameter in SIQS.NET has a default derived from the decimal size of `N`, so nothing here has
to be supplied. The options exist so you can override one and compare — which is the point of the
project. This page is the reference for what each one means, what it defaults to, and when moving it
is likely to help.

`qs --help` prints the same list in short form. The stage tools (`qs-fb`, `qs-sieve`, `qs-filter`,
`qs-linalg`, `qs-sqrt`) accept the options belonging to their own stage, with identical names and
defaults.

> [!TIP]
> Two options are worth knowing before the rest. `--parallelism 1` makes a run's artifacts
> byte-for-byte reproducible, which is what you want when comparing two parameter choices.
> `--trial-sieve-percent 10` stops after a tenth of the relation target, which turns a
> half-hour sieve into a measurement you can repeat.

## How the defaults are picked

The default selectors live in two files and are pure functions of the digit count (and the
factor-base bound where relevant):

- `Factorbase/FactorBaseDefaults.cs` — the smoothness bound.
- `Sieving/SievingParameters.cs` — everything else in the sieve.

Each selector carries the reasoning for its tiers in a comment, including the cases where a tuning
change was measured and rejected. Read those before changing a default; several obvious-looking
improvements have already been tried.

## Supported request limits

All capstone CLI, UI, pipeline, and distributed submissions pass through the same normalized-request
policy before a job directory or background task is created. Target-dependent sieve defaults are
checked again after the factor base supplies the remaining geometry. These are support and
availability limits, not recommended tuning values:

| Control | Maximum | Rationale |
| --- | ---: | --- |
| Target size | 150 decimal digits | Bounds public-service primality and big-integer work beyond the measured C115 profiles. |
| Factor-base bound | 60,000,000 | `PrimeSieve` allocates a directly indexed Boolean array; this is the widest tuned C110+ profile. |
| Explicit multiplier | 1,000,000 | Covers the small Knuth–Schroeppel geometry without admitting arbitrary growth of `kN`. |
| Sieve half interval | 33,554,432 | Widest measured profile; the full `2M+1` interval must also fit the signed 32-bit coordinator representation. |
| Sieve block size | 4,194,304 entries | Bounds each worker's byte sieve and offset maps; it must not exceed `2M+1`. |
| Polynomial count / relation target | 1,000,000,000 / 5,000,000 | Bounds scheduled work and the matrix's 32-bit row space while retaining the widest defaults. |
| Output batch | 1,000,000 records | Bounds per-batch materialization and publication work. |
| A-prime count / window | 16 / 1,024 | Bounds combination and Gray-code family growth; count must not exceed the window. |
| LP1 / LP2 bound | 64,000,000,000 / 1,000,000,000,000 | Keeps residual splitting inside the supported 64-bit/BigInteger paths without unbounded search geometry. |
| Error margin | 256 bits | Bounds false-positive candidate growth. |
| Sieving / linear-algebra parallelism | 256 | Prevents accidental process-sized worker and partition counts; zero still means processor count. |
| Bucket / resieve cutoff | 60,000,000 | Matches the widest factor-base representation; resieving requires a larger enabled bucket cutoff. |
| Dependencies | 64 | One Block Lanczos candidate block; later seeds are retries, not extra dependency harvesting. |

## Run control

| Option | Default | What it does |
| --- | --- | --- |
| `--n <number>` | — | The number to factor, as an alternative to the positional argument. With neither, `qs` reads the number from stdin. |
| `--run-dir <path>` | `runs/<jobId>` | Where run artifacts are written. |
| `--resume <run-dir>` | — | Resume a canceled run from its saved artifacts. Cannot be combined with `--run-dir`. Passing tuning options alongside it overrides the stored parameters for the remaining work. |
| `--trial-sieve-percent <p>` | — | Stop sieving after `p`% of the relation target and report throughput instead of factoring. For timing runs. |
| `--quiet` | off | Print only the factor product, for piping into another tool. |
| `--debug` | off | Print the full per-phase counter dump instead of the summary view. |

## Factor base

| Option | Default | What it does |
| --- | --- | --- |
| `--bound <b>` | Log-linear in the size of `N`, from 1,000 upward, with measured plateaus at C102 (15M), C105 (30M), C108 (40M) and C110+ (60M) | The smoothness bound: primes up to `b` for which `N` is a quadratic residue enter the factor base. Raising it makes each value likelier to be smooth but enlarges the matrix and the sieve's working set. |
| `--multiplier <k>` | Knuth–Schroeppel choice | Sieve `kN` instead of `N`, chosen so the small primes divide more values. Overriding this is mostly of interest when studying how much the multiplier is worth. |

## Sieving

| Option | Default | What it does |
| --- | --- | --- |
| `--sieve-half-interval <m>` | 128 at C13 rising to 1,048,576 at C40, then measured plateaus to 33,554,432 at C113+ | Half-width `M` of the sieve interval `[-M, M]` per polynomial. Larger intervals amortize polynomial setup over more values, at the cost of values further from the origin being larger and so less likely to be smooth. |
| `--polynomial-count <n>` | The full supply available from the A-prime window | Cap on how many polynomials to sieve. |
| `--relations-target <n>` | Factor-base size plus a surplus: +512 below C70, +1% (min 2,048) to C99, +4% (min 10,000) at C100+ | How many usable relations to collect before filtering. The surplus covers rows that filtering discards; too small a surplus means the matrix has no null space and the sieve has to be topped up. |
| `--large-prime-bound <b>` | 8× the factor-base bound, rising to 192× by C75, then a flat 10⁹ at C100+ | The largest single large prime allowed in a partial relation. Partials are cheap to find and combine into full relations during filtering, so a wider bound trades filtering work for sieve time. |
| `--error-margin <bits>` | 0 below C65, rising to 48 at C104+ | Slack subtracted from the log threshold when deciding whether a sieve position is worth trial dividing. Larger margins catch more true smooths and more false positives. |
| `--a-prime-count <s>` | 2 at C19 and below, rising to 10 at C104+ | How many factor-base primes are multiplied to form the leading coefficient `A`. More primes mean more `B` values per `A`, so more polynomials per initialization. |
| `--a-prime-window-size <n>` | Widened from `max(16, 16s)` until the window supplies enough polynomials | The size of the band of factor-base primes the A-primes are drawn from. **Do not narrow this without measuring.** A narrow band makes polynomials so correlated that different `(A, B)` pairs rediscover the same smooth values, and the resulting duplicate relations and co-occurring A-columns leave Block Lanczos almost no extractable null space. |
| `--parallelism <n>` | 0 (every core) | Sieving threads. `1` gives reproducible artifacts. |
| `--sieve-block-size <n>` | 262,144 entries below C70, 524,288 above | Cache block size for block sieving, in sieve entries rather than bytes. Smaller blocks were measured against msieve/YAFU-style ~32 KB blocks and lost: small blocks force each prime's root to be re-advanced far more often. |
| `--bucket-large-prime-cutoff <p>` | 0 below C85, then 1,048,576 (655,360 in the C100–C110 band) | Primes at or above `p` are bucket-sieved: their hits are materialized as per-block lists once per polynomial and replayed during fill, instead of being scattered across a working set far larger than cache. `0` disables the bucket path. |
| `--resieve-large-prime-cutoff <p>` | 0 below C85, then 262,144 | Primes in `[p, bucket cutoff)` are rediscovered by walking their progressions once per block, rather than being tested against every candidate. `0` disables resieving. |
| `--two-large-primes <bool>` | off below C110, on at C110+ | Collect partial relations with two large primes. These are far more common than single-large-prime partials and produce many more filtering cycles, but each one costs a cofactor split. |
| `--large-prime2-bound <b>` | 1.5× the factor-base bound | The largest cofactor accepted as a two-large-prime pair. Wider bounds add graph cycles but grow the partial-relation graph filtering has to walk. |
| `--large-prime2-threshold-bound <b>` | Same as `--large-prime2-bound` | The bound used to grant scan log credit in two-large-prime mode. It does not itself limit which relations are accepted. Clamped down to `--large-prime2-bound` if set higher. |
| `--cofactor-splitter <kind>` | `micro-ecm-stage2` at C110+, otherwise auto-selected from the large-prime bound | Which algorithm splits composite cofactors: `squfof`, `squfof-rho`, `micro-ecm-squfof`, or `micro-ecm-stage2`. All four accept the same relations; they differ only in speed on a given residual size. |

### Sieve-only options

These are accepted by `qs-sieve` but not by `qs`, because the pipeline owns the equivalent decision:

| Option | Default | What it does |
| --- | --- | --- |
| `--factor-base <path>` | `factor_base.txt` | The factor base to sieve against. |
| `--out-dir <path>` | `.` | Where raw relation batches are written. |
| `--batch-size <n>` | 10,000 | Relations per raw batch file. |
| `--trial-relations-target <n>` | — | Stop after `n` raw relations. Mutually exclusive with `--trial-sieve-percent`. |

## Filtering

Filtering options are accepted by `qs-filter`; the pipeline uses the defaults.

| Option | Default | What it does |
| --- | --- | --- |
| `--max-partials-per-prime <n>` | unbounded | Cap on how many partial relations are kept per large prime, bounding the size of the cycle search. |
| `--max-cycle-length <n>` | unbounded | Cap on the length of a partial-relation cycle that filtering will combine into a full relation. |
| `--enable-two-merge <bool>` | `true` | Merge columns of weight two, which shrinks the matrix at the cost of denser rows. |
| `--two-merge-slack <n>` | engine default | How much row-weight growth a two-merge may cause before it is rejected. |
| `--filter-spill-dir <path>` | in memory | Spill candidate relation payloads to this directory instead of holding them in memory. `qs` decides this for itself (`FilteringSpillPolicy` turns it on for large composites, spilling into the run directory); `qs-filter` spills only when you name a directory. |

## Linear algebra

| Option | Default | What it does |
| --- | --- | --- |
| `--max-dependencies <n>` | 64 | Cap (1–64) on verified vectors emitted from one successful 64-column Block Lanczos solve. Later deterministic seeds are failure retries, not a request to accumulate more vectors after success. |
| `--linalg-parallelism <n>` | 0 (every core) | Block Lanczos threads. |

`qs-linalg` additionally accepts `--linalg-seed <n>` (default fixed), the seed for the random blocks
Block Lanczos starts from. The pipeline derives its own retry seeds; if all of them fail identically,
re-running `qs-linalg` with a different seed is the escape hatch.

## Square root

| Option | Default | What it does |
| --- | --- | --- |
| `--continue-after-factor` | off | Keep trying dependencies after the first non-trivial factor. Useful when `N` has more than two prime factors, since one dependency only splits it once. |

## Measuring a change

The build treats warnings as errors and the test suite is fast, but neither will tell you whether a
parameter change helped. For that:

- `SIQS.PerformanceSpy/` runs an end-to-end timing sweep across digit sizes, which is what catches a
  change that helps at C60 and hurts at C100.
- `SIQS.Benchmarks/` holds BenchmarkDotNet suites for individual kernels.
- `--trial-sieve-percent` with `--parallelism 1` gives a repeatable sieve-throughput number without
  running the whole pipeline.

The project's standing rule is in [CONTRIBUTING.md](../CONTRIBUTING.md): never guess that a style or
a parameter is faster. Measure it, and report the target size, before/after wall and CPU time, the
repetition count, and the CPU model.
