using System.Globalization;
using SIQS.Pipeline;

namespace QS;

internal static class TrialSieveReporter
{
    public static void Write(FactorizationJobResult result)
    {
        var phase = result.PhaseSummaries.FirstOrDefault(summary => summary.Phase == SIQS.Contracts.SiqsPhase.Sieving);
        if (phase is null)
        {
            Console.WriteLine("  [trial sieve]  did not complete");
            return;
        }

        static long Get(PhaseSummary summary, string key)
            => summary.Counters.TryGetValue(key, out var value)
                && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed : 0;

        var raw = Get(phase, "raw_relations");
        var target = Get(phase, "trial_raw_target");
        var usable = Get(phase, "usable_relations");
        var full = Get(phase, "full_relations");
        var usefulFull = Math.Max(0, full - Get(phase, "zero_parity_full_relations"));
        var partial = Get(phase, "partial_relations");
        var polynomials = Get(phase, "polynomials");
        var elapsed = phase.ElapsedSeconds ?? 0.0;
        var rawPerSecond = elapsed > 0 ? raw / elapsed : 0.0;
        var rawPerPolynomial = polynomials > 0 ? (double)raw / polynomials : 0.0;
        var cycleContribution = Math.Max(0, usable - usefulFull);
        var cycleRate = partial > 0 ? 100.0 * cycleContribution / partial : 0.0;

        Console.WriteLine($"  [trial sieve]  stopped after {raw}/{target} raw relations; {usable} usable so far versus normal target {Get(phase, "relations_needed")}");
        Console.WriteLine("  [trial meaning]the requested percent sizes this raw-relation stop budget; it is not a percent of polynomials, elapsed time, or total sieve work");
        Console.WriteLine($"  [sample rate]  {rawPerSecond:F1} raw rel/s, {rawPerPolynomial:F3} raw rel/poly, {(polynomials > 0 ? elapsed / polynomials * 1000.0 : 0.0):F3} ms/poly");
        Console.WriteLine($"  [sample yield] { (polynomials > 0 ? (double)usefulFull / polynomials : 0.0):F3} useful full rel/poly; closed-cycle contribution {cycleContribution}/{partial} partials ({cycleRate:F2}%)");

        var one = Get(phase, "one_large_prime_partials");
        var two = Get(phase, "two_large_prime_partials");
        var attempts = Get(phase, "two_large_prime_split_attempts");
        if (one > 0 || two > 0 || attempts > 0)
        {
            Console.WriteLine($"  [large primes]1LP={one}, 2LP={two}, 2LP split={Get(phase, "two_large_prime_split_successes")}/{attempts}");
            Console.WriteLine($"  [2lp residuals]too-small={Get(phase, "two_large_prime_residual_too_small")}, too-large={Get(phase, "two_large_prime_residual_too_large")}, prime={Get(phase, "two_large_prime_residual_prime")}");
        }
    }
}
