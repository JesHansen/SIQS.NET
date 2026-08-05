using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Factorbase;
using Sieving;
using SIQS.Contracts.Files;

namespace SIQS.Benchmarks;

/// <summary>
/// Experiment 39 measurement: does the per-candidate work (exact threshold recompute, Q(x), trial
/// division, residual/cofactor) stay flat as a wide two-large-prime policy multiplies candidate
/// volume, and is steady-state per-candidate allocation ~0? Runs one worker, a fixed raw-relation
/// target, at the normal tight profile and a wide-stress profile, and reports candidate-processing
/// CPU and allocated bytes per candidate for each, plus the marginal per-candidate cost between
/// them (which cancels fixed setup).
/// </summary>
internal static class CandidateScalingTool
{
    private readonly record struct Sample(
        string Profile, long Candidates, double ScanMs, double PolyEvalMs, double TrialDivMs,
        long AllocatedBytes, double WallMs)
    {
        public double PlumbingMs => ScanMs + PolyEvalMs;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 1 || !BigInteger.TryParse(args[0], out var target) || target <= 1)
        {
            Console.Error.WriteLine("usage: --candidate-scaling <target> [rawTarget] [wideLp2Multiplier] [reps]");
            return 1;
        }

        var rawTarget = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 6000;
        var wideMultiplier = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 40.0;
        var reps = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 3;

        var factorBase = FactorBaseGenerator.Generate(new FactorBaseOptions(target)).FactorBase
            ?? throw new InvalidOperationException("Target factored during factor-base precheck.");
        var fbBound = factorBase.Metadata.Bound;
        var normal = SievingParameters.Default(factorBase) with
        {
            Parallelism = 1,
            TrialRawRelationTarget = rawTarget,
        };
        var wideLp2 = checked((long)(fbBound * wideMultiplier));
        var wide = normal with
        {
            EnableTwoLargePrimes = true,
            LargePrime2Bound = wideLp2,
            LargePrime2ThresholdBound = wideLp2,
            CofactorSplitter = CofactorSplitterKinds.SelectFor(wideLp2),
        };

        // Warm-up run of each profile (JIT + tiered compilation).
        _ = SievingEngine.Sieve(factorBase, normal);
        _ = SievingEngine.Sieve(factorBase, wide);

        var normalSample = MedianSample("normal-tight", factorBase, normal, reps);
        var wideSample = MedianSample($"wide-x{wideMultiplier}", factorBase, wide, reps);

        Console.WriteLine(
            "profile,candidates,plumbing_ns_per_cand,scan_ns_per_cand,polyeval_ns_per_cand," +
            "trialdiv_ns_per_cand,bytes_per_cand,wall_ms");
        foreach (var s in new[] { normalSample, wideSample })
        {
            var n = (double)s.Candidates;
            Console.WriteLine(
                $"{s.Profile},{s.Candidates},{s.PlumbingMs * 1e6 / n:F1},{s.ScanMs * 1e6 / n:F1}," +
                $"{s.PolyEvalMs * 1e6 / n:F1},{s.TrialDivMs * 1e6 / n:F1}," +
                $"{s.AllocatedBytes / n:F1},{s.WallMs:F1}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "Plumbing = scan (exact threshold recompute + control flow) + Q(x) eval; the proposal's " +
            "per-candidate pipeline cost. TrialDiv includes the cofactor split (Experiment 37).");
        return 0;
    }

    private static Sample MedianSample(string profile, FactorBaseDocument factorBase, SievingParameters parameters, int reps)
    {
        var samples = new List<Sample>(reps);
        for (var r = 0; r < reps; r++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
            var watch = Stopwatch.StartNew();
            var result = SievingEngine.Sieve(factorBase, parameters);
            watch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            var counters = result.Counters;
            samples.Add(new Sample(
                profile, counters.Candidates, counters.ScanCpuMs, counters.PolyEvalCpuMs,
                counters.TrialDivCpuMs, afterBytes - beforeBytes, watch.Elapsed.TotalMilliseconds));
        }

        samples.Sort((a, b) => a.WallMs.CompareTo(b.WallMs));
        return samples[samples.Count / 2];
    }
}
