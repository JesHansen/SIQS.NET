using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Factorbase;
using Sieving;

namespace SIQS.Benchmarks;

internal static class SmallPrimeVariationComparisonTool
{
    private readonly record struct Configuration(string Name, int SpvBound);

    public static int Run(string[] args)
    {
        if (args.Length is < 1 or > 6 || !BigInteger.TryParse(args[0], out var target) || target <= 1)
        {
            Console.Error.WriteLine(
                "usage: --compare-spv <target> [raw-target] [parallelism] [repetitions] " +
                "[both|off|on|wide|wide-leading|upper] [polynomials]");
            return 1;
        }

        var rawTarget = ParseOptional(args, 1, 10_000);
        var parallelism = ParseOptional(args, 2, 16);
        var repetitions = ParseOptional(args, 3, 3);
        var factorBase = FactorBaseGenerator.Generate(new FactorBaseOptions(target)).FactorBase
            ?? throw new InvalidOperationException("Target factored during factor-base precheck.");
        var defaults = SievingParameters.Default(factorBase) with
        {
            Parallelism = parallelism,
            TrialRawRelationTarget = rawTarget,
            PolynomialCount = ParseOptional(
                args, 5, (int)Math.Min(int.MaxValue,
                    SievingParameters.Default(factorBase).PolynomialCount)),
        };
        var mode = args.Length > 4 ? args[4] : "both";
        Configuration[] configurations = mode switch
        {
            "off" => [new("spv-off", 0)],
            "on" => [new("spv-256", 256)],
            "both" => [new("spv-off", 0), new("spv-256", 256)],
            "wide" =>
            [
                new("spv-256", 256),
                new("spv-384", 384),
                new("spv-512", 512),
                new("spv-768", 768),
                new("spv-1024", 1_024),
            ],
            "wide-leading" =>
            [
                new("spv-256", 256),
                new("spv-512", 512),
                new("spv-768", 768),
                new("spv-1024", 1_024),
            ],
            "upper" =>
            [
                new("spv-512", 512),
                new("spv-768", 768),
            ],
            _ => throw new ArgumentException(
                "Configuration must be both, off, on, wide, wide-leading, or upper."),
        };

        Console.WriteLine(
            "configuration,rep,elapsed_ms,fill_cpu_ms,init_cpu_ms,scan_cpu_ms,spv_cpu_ms," +
            "known_hit_cpu_ms,trial_cpu_ms,polynomials,candidates,raw_relations,usable_relations," +
            "spv_primes,spv_allowance,preliminary_reports,spv_rejected");
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            var ordered = (repetition & 1) == 0 ? configurations.Reverse() : configurations;
            foreach (var configuration in ordered)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var parameters = defaults with
                {
                    SmallPrimeVariationBound = configuration.SpvBound,
                };
                var watch = Stopwatch.StartNew();
                var result = SievingEngine.Sieve(factorBase, parameters);
                watch.Stop();
                var counters = result.Counters;
                Console.WriteLine(
                    $"{configuration.Name},{repetition},{watch.Elapsed.TotalMilliseconds:F3}," +
                    $"{counters.SieveFillCpuMs},{counters.SieveInitCpuMs},{counters.ScanCpuMs}," +
                    $"{counters.SmallPrimeVariationCpuMs},{counters.KnownHitCollectionCpuMs}," +
                    $"{counters.TrialDivCpuMs},{counters.Polynomials},{counters.Candidates}," +
                    $"{counters.RawRelations},{counters.UsableRelations}," +
                    $"{counters.SmallPrimeVariationCount},{counters.SmallPrimeVariationAllowance}," +
                    $"{counters.PreliminaryReports},{counters.SmallPrimeVariationRejected}");
            }
        }

        return 0;
    }

    private static int ParseOptional(string[] args, int index, int defaultValue)
        => args.Length > index
            ? int.Parse(args[index], NumberStyles.None, CultureInfo.InvariantCulture)
            : defaultValue;
}
