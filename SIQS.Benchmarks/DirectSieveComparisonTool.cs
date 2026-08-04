using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Factorbase;
using Sieving;
using SIQS.Contracts.Files;

namespace SIQS.Benchmarks;

internal static class DirectSieveComparisonTool
{
    private readonly record struct Measurement(
        string Kernel, int Repetition, double ElapsedMilliseconds, SievingCounters Counters);

    public static int Run(string[] args)
    {
        if (args.Length is < 1 or > 4 || !BigInteger.TryParse(args[0], out var target) || target <= 1)
        {
            Console.Error.WriteLine(
                "usage: --compare-direct-sieve <target> [raw-relation-target] [parallelism] [repetitions]");
            return 1;
        }

        var rawTarget = ParseOptional(args, 1, 25_000);
        var parallelism = ParseOptional(args, 2, 16);
        var repetitions = ParseOptional(args, 3, 3);
        var generation = FactorBaseGenerator.Generate(new FactorBaseOptions(target));
        var factorBase = generation.FactorBase
            ?? throw new InvalidOperationException("Target factored during factor-base precheck.");
        var baseline = SievingParameters.Default(factorBase) with
        {
            Parallelism = parallelism,
            TrialRawRelationTarget = rawTarget,
        };

        var measurements = new List<Measurement>(2 * repetitions);
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            var bandedFirst = (repetition & 1) == 0;
            if (bandedFirst)
            {
                measurements.Add(Measure("banded", repetition, factorBase, baseline));
                measurements.Add(Measure("generic", repetition, factorBase,
                    baseline with { DisableBandedDirectSieve = true }));
            }
            else
            {
                measurements.Add(Measure("generic", repetition, factorBase,
                    baseline with { DisableBandedDirectSieve = true }));
                measurements.Add(Measure("banded", repetition, factorBase, baseline));
            }
        }

        Console.WriteLine("kernel,rep,elapsed_ms,fill_cpu_ms,init_cpu_ms,polynomials,candidates,raw_relations");
        foreach (var measurement in measurements)
        {
            var counters = measurement.Counters;
            Console.WriteLine(
                $"{measurement.Kernel},{measurement.Repetition},{measurement.ElapsedMilliseconds:F3}," +
                $"{counters.SieveFillCpuMs},{counters.SieveInitCpuMs},{counters.Polynomials}," +
                $"{counters.Candidates},{counters.RawRelations}");
        }

        return 0;
    }

    private static Measurement Measure(
        string kernel, int repetition, FactorBaseDocument factorBase,
        SievingParameters parameters)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var watch = Stopwatch.StartNew();
        var result = SievingEngine.Sieve(factorBase, parameters);
        watch.Stop();
        return new(kernel, repetition, watch.Elapsed.TotalMilliseconds, result.Counters);
    }

    private static int ParseOptional(string[] args, int index, int defaultValue)
        => args.Length > index
            ? int.Parse(args[index], NumberStyles.None, CultureInfo.InvariantCulture)
            : defaultValue;
}
