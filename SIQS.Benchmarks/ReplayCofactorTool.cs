using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Sieving;

namespace SIQS.Benchmarks;

/// <summary>
/// Replays a captured residual corpus through one or more candidate cofactor splitters and reports,
/// per splitter: throughput, residuals factored, accepted two-large-prime pairs, an accepted-pair
/// checksum (so two splitters can be proven to accept the same set), and success rate by residual
/// bit size. This is the seconds-per-run measurement foundation for experiments 37 and 38.
/// </summary>
internal static class ReplayCofactorTool
{
    private readonly record struct Arm(string Name, Func<ulong, ulong> Split);

    private sealed class Score
    {
        public double MedianMilliseconds;
        public int Factored;
        public int Accepted;
        public ulong AcceptedChecksum;
        public readonly SortedDictionary<int, (int Accepted, int Seen)> ByBits = new();
    }

    public static int Run(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine(
                "usage: --replay-cofactor <corpus-file> <lp2Bound> <factorBaseBound> <splitter|all> <reps>");
            Console.Error.WriteLine(
                "  splitter: squfof | squfof-rho | micro-ecm-squfof | <exp37/38 arms> | all");
            return 1;
        }

        var corpusPath = args[0];
        var lp2Bound = ulong.Parse(args[1], CultureInfo.InvariantCulture);
        var factorBaseBound = ulong.Parse(args[2], CultureInfo.InvariantCulture);
        var splitterToken = args[3];
        var reps = int.Parse(args[4], CultureInfo.InvariantCulture);

        var corpus = File.ReadLines(corpusPath)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => ulong.Parse(line, CultureInfo.InvariantCulture))
            .ToArray();
        if (corpus.Length == 0)
        {
            Console.Error.WriteLine($"corpus {corpusPath} is empty");
            return 1;
        }

        Console.WriteLine(
            $"corpus={Path.GetFileName(corpusPath)} residuals={corpus.Length} " +
            $"bits={corpus.Min(BitLength)}..{corpus.Max(BitLength)} " +
            $"fb_bound={factorBaseBound} lp2_bound={lp2Bound} reps={reps}");

        var arms = CofactorReplayArms.Resolve(splitterToken).ToArray();
        if (arms.Length == 0)
        {
            Console.Error.WriteLine($"unknown splitter '{splitterToken}'");
            return 1;
        }

        var scores = arms.ToDictionary(static arm => arm.Name, static _ => new Score());
        var perPass = arms.ToDictionary(static arm => arm.Name, static _ => new List<double>());

        // 3 untimed warm-up passes over the full corpus for every arm (tiered-JIT trap guard).
        for (var warm = 0; warm < 3; warm++)
        {
            foreach (var arm in arms)
            {
                var sink = 0UL;
                foreach (var value in corpus) sink ^= arm.Split(value);
                GC.KeepAlive(sink);
            }
        }

        for (var rep = 0; rep < reps; rep++)
        {
            // Reverse arm order on alternate passes to cancel any ordering/thermal bias.
            var order = (rep & 1) == 0 ? arms : arms.Reverse().ToArray();
            foreach (var arm in order)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var factored = 0;
                var accepted = 0;
                var checksum = 0UL;
                var watch = Stopwatch.StartNew();
                foreach (var value in corpus)
                {
                    var factor = arm.Split(value);
                    if (factor <= 1 || factor >= value || value % factor != 0) continue;
                    factored++;
                    var other = value / factor;
                    if (factor > factorBaseBound && other > factorBaseBound &&
                        factor <= lp2Bound && other <= lp2Bound &&
                        CofactorPrimality64.IsPrime(factor) && CofactorPrimality64.IsPrime(other))
                    {
                        accepted++;
                        var lo = Math.Min(factor, other);
                        var hi = Math.Max(factor, other);
                        checksum ^= unchecked(lo * 0x9E3779B97F4A7C15UL + hi * 0xC2B2AE3D27D4EB4FUL);
                    }
                }

                watch.Stop();
                perPass[arm.Name].Add(watch.Elapsed.TotalMilliseconds);

                // Record yield/checksum once (deterministic across passes); assert stability otherwise.
                var score = scores[arm.Name];
                if (score.Factored == 0 && score.Accepted == 0 && score.AcceptedChecksum == 0)
                {
                    score.Factored = factored;
                    score.Accepted = accepted;
                    score.AcceptedChecksum = checksum;
                }
            }
        }

        // Per-bit success (single untimed accounting pass per arm).
        foreach (var arm in arms)
        {
            var score = scores[arm.Name];
            foreach (var value in corpus)
            {
                var bits = BitLength(value);
                var bucket = score.ByBits.TryGetValue(bits, out var existing) ? existing : (Accepted: 0, Seen: 0);
                bucket.Seen++;
                var factor = arm.Split(value);
                if (factor > 1 && factor < value && value % factor == 0)
                {
                    var other = value / factor;
                    if (factor > factorBaseBound && other > factorBaseBound &&
                        factor <= lp2Bound && other <= lp2Bound &&
                        CofactorPrimality64.IsPrime(factor) && CofactorPrimality64.IsPrime(other))
                    {
                        bucket.Accepted++;
                    }
                }

                score.ByBits[bits] = bucket;
            }

            score.MedianMilliseconds = Median(perPass[arm.Name]);
        }

        Console.WriteLine("algorithm,median_ms,residuals_per_second,factored,accepted,accepted_checksum");
        foreach (var arm in arms)
        {
            var score = scores[arm.Name];
            var perSecond = corpus.Length * 1000.0 / score.MedianMilliseconds;
            Console.WriteLine(
                $"{arm.Name},{score.MedianMilliseconds:F3},{perSecond:F0}," +
                $"{score.Factored},{score.Accepted},{score.AcceptedChecksum}");
        }

        // Accepted-by-bitsize table for the primary (first) arm to characterise the corpus.
        Console.WriteLine();
        Console.WriteLine("accepted_by_bits (bits: accepted/seen per arm):");
        var allBits = scores.Values.SelectMany(static s => s.ByBits.Keys).Distinct().OrderBy(static b => b);
        Console.Write("bits");
        foreach (var arm in arms) Console.Write($",{arm.Name}");
        Console.WriteLine();
        foreach (var bits in allBits)
        {
            Console.Write(bits.ToString(CultureInfo.InvariantCulture));
            foreach (var arm in arms)
            {
                var by = scores[arm.Name].ByBits;
                var cell = by.TryGetValue(bits, out var v) ? $"{v.Accepted}/{v.Seen}" : "0/0";
                Console.Write($",{cell}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.OrderBy(static v => v).ToArray();
        var mid = sorted.Length / 2;
        return (sorted.Length & 1) == 1 ? sorted[mid] : 0.5 * (sorted[mid - 1] + sorted[mid]);
    }

    private static int BitLength(ulong value) => 64 - BitOperations.LeadingZeroCount(value);
}
