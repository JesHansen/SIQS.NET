using System.Diagnostics;
using System.Globalization;
using Sieving;

namespace SIQS.Benchmarks;

internal static class ResidualCorpusBenchmarkTool
{
    private static ulong _factorBaseBound = 1_750_000;
    private static ulong _largePrime2Bound = 10_000_000;

    private readonly record struct MicroConfiguration(int B1, int Curves, int B2Multiplier)
    {
        public override string ToString() => $"micro-ecm B1={B1} curves={Curves} B2x={B2Multiplier}";
    }

    private readonly record struct Measurement(
        string Name, double Milliseconds, int Factors, int Accepted, ulong Checksum);

    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: --benchmark-residuals <corpus-file>... [--take N] [--lp2-bound N]");
            return 1;
        }

        var take = int.MaxValue;
        var quick = false;
        var paths = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--take" && i + 1 < args.Length)
            {
                take = int.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--lp2-bound" && i + 1 < args.Length)
            {
                _largePrime2Bound = ulong.Parse(args[++i], CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--quick")
            {
                quick = true;
            }
            else
            {
                paths.Add(args[i]);
            }
        }

        var corpus = paths
            .SelectMany(File.ReadLines)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => ulong.Parse(line, CultureInfo.InvariantCulture))
            .Take(take)
            .ToArray();
        Console.WriteLine($"residuals={corpus.Length} bits={corpus.Min(BitLength)}..{corpus.Max(BitLength)}");

        // Warm the JIT and static data before measuring.
        foreach (var value in corpus.Take(Math.Min(32, corpus.Length)))
        {
            _ = CofactorFactorizer.Squfof64(value);
            _ = MicroEcm64.TryFactor(value, 47, 1);
        }

        var measurements = new List<Measurement>
        {
            Measure("squfof", corpus, CofactorFactorizer.Squfof64),
        };
        MicroConfiguration[] configurations = quick
            ? [new(47, 1, 0), new(70, 1, 0), new(125, 1, 0), new(205, 1, 0)]
            :
        [
            new(47, 1, 0),
            new(47, 2, 0),
            new(47, 4, 0),
            new(47, 8, 0),
            new(47, 16, 0),
            new(47, 32, 0),
            new(70, 8, 0),
            new(70, 16, 0),
            new(70, 32, 0),
            new(125, 8, 0),
            new(125, 16, 0),
            new(70, 8, 25),
        ];
        measurements.AddRange(configurations.Select(configuration => Measure(
            configuration.ToString(), corpus,
            value => MicroEcm64.TryFactor(
                value, configuration.B1, configuration.Curves, configuration.B2Multiplier))));
        var hybridConfigurations = quick
            ? configurations
            : [new(47, 1, 0), new(47, 2, 0), new(47, 4, 0)];
        foreach (var configuration in hybridConfigurations)
        {
            measurements.Add(Measure($"{configuration} then squfof", corpus, value =>
            {
                var factor = MicroEcm64.TryFactor(value, configuration.B1, configuration.Curves);
                return factor > 1 ? factor : CofactorFactorizer.Squfof64(value);
            }));
        }
        measurements.Add(Measure("adaptive one-curve micro-ecm then squfof", corpus, value =>
        {
            var factor = MicroEcm64.TryFactor(value, MicroEcm64.SelectPrefilterStage1Bound(value), 1);
            return factor > 1 ? factor : CofactorFactorizer.Squfof64(value);
        }));

        Console.WriteLine("algorithm,elapsed_ms,residuals_per_second,factors,accepted,checksum");
        foreach (var result in measurements)
        {
            var perSecond = corpus.Length * 1000.0 / result.Milliseconds;
            Console.WriteLine($"{result.Name},{result.Milliseconds:F3},{perSecond:F1},{result.Factors},{result.Accepted},{result.Checksum}");
        }

        return 0;
    }

    private static Measurement Measure(string name, ulong[] corpus, Func<ulong, ulong> split)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var factors = 0;
        var accepted = 0;
        var checksum = 0UL;
        var watch = Stopwatch.StartNew();
        foreach (var value in corpus)
        {
            var factor = split(value);
            if (factor <= 1 || factor >= value || value % factor != 0) continue;
            factors++;
            var other = value / factor;
            if (factor > _factorBaseBound && other > _factorBaseBound
                && factor <= _largePrime2Bound && other <= _largePrime2Bound
                && CofactorPrimality64.IsPrime(factor) && CofactorPrimality64.IsPrime(other))
            {
                accepted++;
            }
            checksum ^= factor + 0x9E3779B97F4A7C15UL * other;
        }

        watch.Stop();
        return new(name, watch.Elapsed.TotalMilliseconds, factors, accepted, checksum);
    }

    private static int BitLength(ulong value) => 64 - System.Numerics.BitOperations.LeadingZeroCount(value);
}
