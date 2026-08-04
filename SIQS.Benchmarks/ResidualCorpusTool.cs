using System.Globalization;
using System.Numerics;
using Factorbase;
using Sieving;

namespace SIQS.Benchmarks;

/// <summary>
/// Captures the composite residuals that the cofactor splitter is actually asked to split, under a
/// chosen two-large-prime (LP2) profile, so experiments 37/38 can be measured on a representative
/// distribution. The sieve runs with the real digit-size defaults for the target; only the LP2 bound
/// (and the matching scan-credit threshold) are widened by <c>lp2-multiplier × factorBaseBound</c>.
/// </summary>
internal static class ResidualCorpusTool
{
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 5 || !BigInteger.TryParse(args[0], out var target) || target <= 1)
        {
            Console.Error.WriteLine(
                "usage: --capture-residuals <target> <output-file> [raw-relation-target] [lp2-multiplier] [splitter]");
            Console.Error.WriteLine(
                "  Uses the real digit-size sieving defaults for <target>; lp2-multiplier scales the");
            Console.Error.WriteLine(
                "  factor-base bound to set LP2 (default 1.5 = the production tight profile).");
            return 1;
        }

        var rawTarget = args.Length >= 3
            ? int.Parse(args[2], NumberStyles.None, CultureInfo.InvariantCulture)
            : 40_000;
        var lp2Multiplier = args.Length >= 4
            ? double.Parse(args[3], CultureInfo.InvariantCulture)
            : 1.5;

        var generation = FactorBaseGenerator.Generate(new FactorBaseOptions(target));
        var factorBase = generation.FactorBase
            ?? throw new InvalidOperationException("Target factored during factor-base precheck.");
        var factorBaseBound = factorBase.Metadata.Bound;
        var largePrime2Bound = checked((long)(factorBaseBound * lp2Multiplier));

        var defaults = SievingParameters.Default(factorBase);
        var splitter = args.Length == 5 && CofactorSplitterKinds.TryParse(args[4], out var parsedSplitter)
            ? parsedSplitter
            : CofactorSplitterKinds.SelectFor(largePrime2Bound);
        var parameters = defaults with
        {
            EnableTwoLargePrimes = true,
            LargePrime2Bound = largePrime2Bound,
            LargePrime2ThresholdBound = largePrime2Bound,
            CofactorSplitter = splitter,
            TrialRawRelationTarget = rawTarget,
            CaptureCompositeResiduals = true,
        };

        var started = DateTime.UtcNow;
        var result = SievingEngine.Sieve(factorBase, parameters);
        var elapsed = DateTime.UtcNow - started;

        var captured = result.Counters.CapturedCompositeResiduals ?? [];
        var unique = captured.Distinct().OrderBy(static value => value).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        File.WriteAllLines(args[1], unique.Select(static value => value.ToString(CultureInfo.InvariantCulture)));

        var digits = BigInteger.Abs(target).ToString().Length;
        Console.WriteLine($"target_digits={digits} target={target}");
        Console.WriteLine(
            $"factor_base={factorBase.Entries.Count} factor_base_bound={factorBaseBound} " +
            $"half_interval={parameters.SieveHalfInterval} a_primes={parameters.APrimeCount}");
        Console.WriteLine(
            $"lp2_multiplier={lp2Multiplier.ToString(CultureInfo.InvariantCulture)} " +
            $"lp2_bound={largePrime2Bound} splitter={splitter.ToToken()} raw_relation_target={rawTarget}");
        Console.WriteLine(
            $"raw_relations={result.Counters.RawRelations} polynomials={result.Counters.Polynomials} " +
            $"two_large_prime_partials={result.Counters.TwoLargePrimePartials}");
        Console.WriteLine(
            $"split_attempts={result.Counters.TwoLargePrimeSplitAttempts} " +
            $"reached_factorizer={captured.Count} unique_residuals={unique.Length}");
        Console.WriteLine(
            $"screen_reject_too_small={result.Counters.TwoLargePrimeResidualTooSmall} " +
            $"too_large={result.Counters.TwoLargePrimeResidualTooLarge} " +
            $"small_factor={result.Counters.TwoLargePrimeResidualSmallFactor} " +
            $"prime={result.Counters.TwoLargePrimeResidualPrime}");
        Console.WriteLine(
            $"residual_bits_le48={result.Counters.TwoLargePrimeResidualBitsLe48} " +
            $"le64={result.Counters.TwoLargePrimeResidualBitsLe64} " +
            $"gt64={result.Counters.TwoLargePrimeResidualBitsGt64}");
        WriteBitHistogram(unique);
        Console.WriteLine($"elapsed={elapsed.TotalSeconds:F3}s output={args[1]}");
        return 0;
    }

    private static void WriteBitHistogram(ulong[] residuals)
    {
        if (residuals.Length == 0)
        {
            Console.WriteLine("bit_histogram: (empty)");
            return;
        }

        var counts = new SortedDictionary<int, int>();
        foreach (var value in residuals)
        {
            var bits = 64 - System.Numerics.BitOperations.LeadingZeroCount(value);
            counts[bits] = counts.TryGetValue(bits, out var existing) ? existing + 1 : 1;
        }

        Console.WriteLine("bit_histogram (bits:count):");
        foreach (var (bits, count) in counts)
        {
            Console.WriteLine($"  {bits}:{count}");
        }
    }
}
