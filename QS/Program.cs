using System.Text;
using QS;
using QS.Presentation;
using SIQS.Contracts;
using SIQS.Contracts.Cli;

// Every option the pipeline accepts is listed here. The defaults are all derived from the size of
// N, so this is a tuning surface rather than a set of required inputs: the point of printing the
// whole list is that a reader can find the knob without reading FactorizationCommandHandler.
const string Usage = """
usage: qs <number> [options]
       qs --resume <run-dir> [options]
       qs [options]                      (reads the number from stdin)

Output
  --quiet                         print only the factor product, for pipelines
  --debug                         print the full per-phase diagnostic output
  --help, -h                      print this message

Run control
  --n <number>                    the number to factor, as an alternative to the positional form
  --run-dir <path>                write run artifacts here instead of runs/<jobId>
  --resume <run-dir>              resume a canceled run from its saved artifacts
  --trial-sieve-percent <p>       stop after p% of the relation target, for timing runs

Factor base
  --bound <b>                     smoothness bound
  --multiplier <k>                Knuth-Schroeppel multiplier

Sieving
  --sieve-half-interval <m>       half-width M of the sieve interval [-M, M]
  --polynomial-count <n>          cap on the number of polynomials to sieve
  --relations-target <n>          how many usable relations to collect before filtering
  --large-prime-bound <b>         largest single large prime kept in a partial relation
  --error-margin <bits>           slack subtracted from the log threshold when picking candidates
  --a-prime-count <s>             number of factor-base primes multiplied to form the A coefficient
  --a-prime-window-size <n>       size of the band of primes those A-primes are drawn from
  --parallelism <n>               sieving threads; 0 uses every core, 1 gives reproducible artifacts
  --sieve-block-size <n>          cache block size in sieve entries
  --bucket-large-prime-cutoff <p> primes >= p are bucket-sieved; 0 disables bucket sieving
  --resieve-large-prime-cutoff <p> primes in [p, bucket cutoff) are resieved; 0 disables resieving
  --two-large-primes <bool>       collect double-large-prime partials
  --large-prime2-bound <b>        largest cofactor accepted as a two-large-prime pair
  --large-prime2-threshold-bound <b> log-threshold bound for admitting a two-large-prime candidate
  --cofactor-splitter <kind>      squfof | squfof-rho | micro-ecm-squfof | micro-ecm-stage2

Linear algebra
  --max-dependencies <n>          cap on the null-space vectors to extract
  --linalg-parallelism <n>        Block Lanczos threads; 0 uses every core

Square root
  --continue-after-factor         keep trying dependencies after the first non-trivial factor

Every value above defaults to a tuned choice made from the size of N. See docs/tuning.md for
what each one does and how the defaults are selected.
""";

// The default Windows console code page best-fit-maps the TUI glyphs (✔, ×, →, bars, spinner)
// to '?' before they reach the terminal. UTF-8 output (without a BOM, so redirected output stays
// clean) lets them through. Guarded because a detached/redirected process may have no console.
try { Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false); }
catch (IOException) { /* no console attached; nothing to configure */ }

try
{
    if (CommandLine.IsHelpRequested(args))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    var cli = CommandLine.Parse(args,
        CommandLineSyntax.FlagAware(allowPositional: true, "quiet", "debug", "continue-after-factor"));
    var presenter = RunPresenterFactory.Create(cli);

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };

    var execution = await new FactorizationCommandHandler().ExecuteAsync(cli, presenter, cancellation.Token);
    presenter.ShowOutcome(execution);

    var result = execution.Result;
    return execution.TrialSieve ? 0
        : result.Status is JobStatus.CompletedFactorFound or JobStatus.CompletedTrivialFactor ? 0
        : result.Status is JobStatus.CompletedNoFactor or JobStatus.CompletedPrime ? 2 : 1;
}
catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException
    or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
{
    // The full option list is a screenful; an error is not the moment to dump it.
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine("usage: qs <number> [options]   (run 'qs --help' for the full option list)");
    return 1;
}
