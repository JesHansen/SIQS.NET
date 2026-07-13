using System.Globalization;
using System.Numerics;
using SIQS.Contracts;
using SIQS.Pipeline;

namespace QS;

/// <summary>Application service that translates capstone CLI input into a pipeline run.</summary>
internal sealed class FactorizationCommandHandler
{
    public async Task<FactorizationCommandResult> ExecuteAsync(
        CliArguments cli,
        IProgress<SiqsProgressEvent> progress,
        CancellationToken cancellationToken)
    {
        var resumeDirectory = cli.GetOptional("resume");
        if (resumeDirectory is "true" or "True" or "TRUE")
        {
            throw new FormatException("--resume requires a run directory.");
        }

        var pipeline = new SiqsPipeline();
        BigInteger target;
        FactorizationRequest? request;
        string artifactDirectory;
        var trial = false;
        var suppliedTarget = cli.Positional ?? cli.GetOptional("n");
        if (resumeDirectory is not null)
        {
            if (cli.GetOptional("run-dir") is not null)
            {
                throw new FormatException("--resume cannot be combined with --run-dir.");
            }

            var stored = pipeline.LoadJob(resumeDirectory);
            target = ParseTarget(suppliedTarget ?? stored.Parameters.GetValueOrDefault("target_n") ?? stored.TargetN);
            request = suppliedTarget is not null || HasResumeParameterOverrides(cli) ? BuildRequest(target, cli, null) : null;
            artifactDirectory = resumeDirectory;
        }
        else
        {
            target = ParseTarget(suppliedTarget ?? ReadTargetFromRedirectedInput()
                ?? throw new FormatException("A number to factor is required (pass it as the first argument, with --n, or via stdin)."));
            request = BuildRequest(target, cli, cli.GetOptional("run-dir"));
            artifactDirectory = request.RunDirectory ?? string.Empty;
            trial = request.TrialSievePercent is not null;
        }

        Console.WriteLine($"  N = {target}  ({target.ToString().Length} digits)");
        Console.WriteLine();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = resumeDirectory is not null
            ? await pipeline.ResumeAsync(resumeDirectory, request, progress, cancellationToken)
            : await pipeline.RunAsync(request!, progress, cancellationToken);
        watch.Stop();
        return new FactorizationCommandResult(target, result, artifactDirectory, trial, watch.Elapsed);
    }

    private static FactorizationRequest BuildRequest(BigInteger n, CliArguments cli, string? runDirectory)
        => new(n, runDirectory, cli.GetDouble("trial-sieve-percent"))
        {
            FactorBase = new FactorBaseRunOptions
            {
                Bound = cli.GetLong("bound"),
                Multiplier = cli.GetOptional("multiplier") is { } multiplier ? BigInteger.Parse(multiplier, CultureInfo.InvariantCulture) : null,
            },
            Sieving = new SievingRunOptions
            {
                HalfInterval = cli.GetLong("sieve-half-interval"),
                PolynomialCount = cli.GetLong("polynomial-count"),
                RelationTarget = cli.GetInt("relations-target"),
                LargePrimeBound = cli.GetLong("large-prime-bound"),
                ErrorMargin = cli.GetInt("error-margin"),
                APrimeCount = cli.GetInt("a-prime-count"),
                APrimeWindowSize = cli.GetInt("a-prime-window-size"),
                Parallelism = cli.GetInt("parallelism"),
                BlockSize = cli.GetInt("sieve-block-size"),
                BucketLargePrimeCutoff = cli.GetInt("bucket-large-prime-cutoff"),
                ResieveLargePrimeCutoff = cli.GetInt("resieve-large-prime-cutoff"),
                EnableTwoLargePrimes = cli.GetBool("two-large-primes"),
                LargePrime2Bound = cli.GetLong("large-prime2-bound"),
                LargePrime2ThresholdBound = cli.GetLong("large-prime2-threshold-bound"),
                CofactorSplitter = cli.GetOptional("cofactor-splitter"),
            },
            LinearAlgebra = new LinearAlgebraRunOptions
            {
                MaxDependencies = cli.GetInt("max-dependencies"),
                Parallelism = cli.GetInt("linalg-parallelism"),
            },
            SquareRoot = new SquareRootRunOptions
            {
                ContinueAfterFactor = cli.GetFlag("continue-after-factor"),
            },
        };

    private static bool HasResumeParameterOverrides(CliArguments cli) => cli.HasAny(
        "bound", "multiplier", "sieve-half-interval", "polynomial-count", "relations-target", "large-prime-bound",
        "error-margin", "a-prime-count", "a-prime-window-size", "trial-sieve-percent", "max-dependencies",
        "parallelism", "linalg-parallelism", "sieve-block-size", "bucket-large-prime-cutoff", "resieve-large-prime-cutoff",
        "two-large-primes", "large-prime2-bound", "large-prime2-threshold-bound", "cofactor-splitter", "continue-after-factor");

    private static BigInteger ParseTarget(string text)
        => BigInteger.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 1
            ? value
            : throw new FormatException("The number to factor must be a decimal integer greater than 1.");

    private static string? ReadTargetFromRedirectedInput()
    {
        if (!Console.IsInputRedirected) return null;
        while (Console.In.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length > 0) return line;
        }
        return null;
    }
}

internal sealed record FactorizationCommandResult(
    BigInteger Target,
    FactorizationJobResult Result,
    string ArtifactDirectory,
    bool TrialSieve,
    TimeSpan Elapsed);
