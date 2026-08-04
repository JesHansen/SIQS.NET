using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Sieving;

namespace SIQS.Benchmarks;

/// <summary>
/// Measures how much of the cofactor path the pre-split screens (small-factor + base-2 PRP) actually
/// cost, relative to the ECM stage-two split, so Experiment 38 can decide whether batching the
/// screens can move the end-to-end number at all. Times each phase on a corpus with warm-up and GC
/// between passes.
/// </summary>
internal static class ScreenShareTool
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: --screen-share <corpus-file> <fbBound> <reps> [nominalPerComposite]");
            return 1;
        }

        var corpus = File.ReadLines(args[0])
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => ulong.Parse(line, CultureInfo.InvariantCulture))
            .ToArray();
        var fbBound = ulong.Parse(args[1], CultureInfo.InvariantCulture);
        var reps = int.Parse(args[2], CultureInfo.InvariantCulture);
        // How many nominal 2LP attempts (residuals that hit the screens) occur per composite that
        // reaches the splitter, from the capture telemetry (split_attempts / reached_factorizer).
        var nominalPerComposite = args.Length > 3
            ? double.Parse(args[3], CultureInfo.InvariantCulture)
            : 1.0;

        Console.WriteLine(
            $"corpus={System.IO.Path.GetFileName(args[0])} residuals={corpus.Length} " +
            $"bits={corpus.Min(BitLength)}..{corpus.Max(BitLength)} fb_bound={fbBound} " +
            $"nominal_per_composite={nominalPerComposite}");

        // Warm-up.
        for (var w = 0; w < 3; w++)
        {
            var s = 0UL;
            foreach (var v in corpus)
            {
                s ^= CofactorPrimality64.HasSmallFactorAtOrBelow(v, fbBound) ? 1UL : 0UL;
                s ^= CofactorPrimality64.IsBase2ProbablePrime(v) ? 1UL : 0UL;
                s ^= MicroEcm64.TryFactorStage2(v, 300, 12_000, 10);
            }

            GC.KeepAlive(s);
        }

        var screenMs = MedianOf(reps, () =>
        {
            var sink = 0;
            foreach (var v in corpus)
            {
                if (CofactorPrimality64.HasSmallFactorAtOrBelow(v, fbBound)) { sink++; continue; }
                if (CofactorPrimality64.IsBase2ProbablePrime(v)) { sink++; }
            }

            return sink;
        });

        var splitMs = MedianOf(reps, () =>
        {
            var sink = 0UL;
            foreach (var v in corpus)
            {
                sink ^= MicroEcm64.TryFactorStage2(v, 300, 12_000, 10);
            }

            return (int)sink;
        });

        var screenPerResidualNs = screenMs * 1e6 / corpus.Length;
        var splitPerResidualNs = splitMs * 1e6 / corpus.Length;
        // Screen runs on every nominal attempt; split runs only on composites reaching the factorizer.
        var screenShare = nominalPerComposite * screenPerResidualNs
            / (nominalPerComposite * screenPerResidualNs + splitPerResidualNs);

        Console.WriteLine($"scalar_screen_ns_per_residual={screenPerResidualNs:F1}");
        Console.WriteLine($"ecm_split_ns_per_composite={splitPerResidualNs:F1}");
        Console.WriteLine(
            $"estimated_screen_share_of_cofactor_cpu={screenShare * 100:F1}%  " +
            $"(screens run {nominalPerComposite}x more often than splits)");
        Console.WriteLine(
            $"max_possible_speedup_from_free_screens={1.0 / (1.0 - screenShare):F2}x");
        return 0;
    }

    private static double MedianOf(int reps, Func<int> action)
    {
        var times = new List<double>(reps);
        for (var r = 0; r < reps; r++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var watch = Stopwatch.StartNew();
            var sink = action();
            watch.Stop();
            GC.KeepAlive(sink);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        return times[times.Count / 2];
    }

    private static int BitLength(ulong value) => 64 - BitOperations.LeadingZeroCount(value);
}
