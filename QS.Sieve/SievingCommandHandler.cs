using System.Diagnostics;
using QsSieve;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using Sieving;

namespace QsSieve;

/// <summary>Loads sieving inputs, resolves parameters, executes sieving, and writes raw batches.</summary>
internal sealed class SievingCommandHandler
{
    public SievingCommandResult Execute(CliArguments cli, IProgress<SiqsProgressEvent>? progress)
    {
        var factorBasePath = cli.GetOptional("factor-base") ?? "factor_base.txt";
        var outDir = cli.GetOptional("out-dir") ?? ".";
        if (!File.Exists(factorBasePath))
        {
            throw new FormatException($"Factor base file not found: {factorBasePath}");
        }

        var factorBase = FactorBaseFile.Parse(File.ReadAllText(factorBasePath));
        var meta = factorBase.Metadata;
        if (meta.ScaledN != meta.TargetN * meta.Multiplier)
        {
            throw new FormatException("Factor base metadata is inconsistent: scaled_n != target_n * multiplier.");
        }

        var defaults = SievingParameters.Default(factorBase);
        var parameters = defaults with
        {
            SieveHalfInterval = cli.GetOptionalLong("sieve-half-interval") ?? defaults.SieveHalfInterval,
            RelationTarget = cli.GetOptionalInt("relations-target") ?? defaults.RelationTarget,
            LargePrimeBound = cli.GetOptionalLong("large-prime-bound") ?? defaults.LargePrimeBound,
            ErrorMargin = cli.GetOptionalInt("error-margin") ?? defaults.ErrorMargin,
            OutputBatchSize = cli.GetOptionalInt("batch-size") ?? defaults.OutputBatchSize,
            APrimeCount = cli.GetOptionalInt("a-prime-count") ?? defaults.APrimeCount,
            APrimeWindowSize = cli.GetOptionalInt("a-prime-window-size") ?? defaults.APrimeWindowSize,
            Parallelism = cli.GetOptionalInt("parallelism") ?? defaults.Parallelism,
            SieveBlockSize = cli.GetOptionalInt("sieve-block-size") ?? defaults.SieveBlockSize,
            BucketLargePrimeCutoff = cli.GetOptionalInt("bucket-large-prime-cutoff") ?? defaults.BucketLargePrimeCutoff,
            ResieveLargePrimeCutoff = cli.GetOptionalInt("resieve-large-prime-cutoff") ?? defaults.ResieveLargePrimeCutoff,
            EnableTwoLargePrimes = cli.GetOptionalBool("two-large-primes") ?? defaults.EnableTwoLargePrimes,
            LargePrime2Bound = cli.GetOptionalLong("large-prime2-bound") ?? defaults.LargePrime2Bound,
            LargePrime2ThresholdBound = cli.GetOptionalLong("large-prime2-threshold-bound") ?? defaults.LargePrime2ThresholdBound,
        };
        parameters = parameters with
        {
            PolynomialCount = cli.GetOptionalLong("polynomial-count")
                ?? SievingParameters.AvailablePolynomialSupply(parameters.APrimeWindowSize, parameters.APrimeCount),
        };
        if (cli.GetOptionalLong("large-prime2-threshold-bound") is null && parameters.LargePrime2ThresholdBound > parameters.LargePrime2Bound)
        {
            parameters = parameters with { LargePrime2ThresholdBound = parameters.LargePrime2Bound };
        }

        var trialPercent = cli.GetOptionalDouble("trial-sieve-percent");
        var explicitTrialTarget = cli.GetOptionalInt("trial-relations-target");
        if (trialPercent is not null && explicitTrialTarget is not null)
        {
            throw new FormatException("Use either --trial-sieve-percent or --trial-relations-target, not both.");
        }

        if (trialPercent is { } percent)
        {
            if (percent <= 0.0 || percent > 100.0)
            {
                throw new FormatException("--trial-sieve-percent must be greater than 0 and at most 100.");
            }

            parameters = parameters with { TrialRawRelationTarget = Math.Max(1, (int)Math.Ceiling(parameters.RelationTarget * percent / 100.0)) };
        }
        else if (explicitTrialTarget is { } trialTarget)
        {
            parameters = parameters with { TrialRawRelationTarget = trialTarget };
        }

        if (parameters.SieveHalfInterval < 1 || parameters.LargePrimeBound < 1 || parameters.RelationTarget < 1
            || parameters.OutputBatchSize < 1 || parameters.APrimeCount < 1 || parameters.APrimeWindowSize < 1
            || parameters.Parallelism < 0 || parameters.SieveBlockSize < 0 || parameters.BucketLargePrimeCutoff < 0
            || parameters.ResieveLargePrimeCutoff < 0 || parameters.LargePrime2Bound < 0
            || parameters.LargePrime2ThresholdBound < 0 || (parameters.EnableTwoLargePrimes && parameters.LargePrime2Bound < 1)
            || (parameters.EnableTwoLargePrimes && parameters.LargePrime2ThresholdBound < 1)
            || (parameters.EnableTwoLargePrimes && parameters.LargePrime2ThresholdBound > parameters.LargePrime2Bound)
            || parameters.TrialRawRelationTarget is <= 0)
        {
            throw new FormatException("Configured sieving parameters are invalid.");
        }

        Directory.CreateDirectory(outDir);
        var metadata = new RawRelationsMetadata(meta.TargetN, meta.Multiplier, meta.ScaledN, meta.Bound,
            parameters.LargePrimeBound, parameters.EnableTwoLargePrimes ? parameters.LargePrime2Bound : null);
        var sink = new RawRelationBatchFileSink(outDir, metadata, parameters.OutputBatchSize);
        var stopwatch = Stopwatch.StartNew();
        var counters = SievingEngine.Sieve(factorBase, parameters, sink, progress);
        stopwatch.Stop();
        return new SievingCommandResult(outDir, parameters, counters, stopwatch.Elapsed);
    }
}

internal sealed record SievingCommandResult(
    string OutputDirectory,
    SievingParameters Parameters,
    SievingCounters Counters,
    TimeSpan Elapsed);
