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
        var partial = Get(phase, "partial_relations");
        var polynomials = Get(phase, "polynomials");
        var elapsed = phase.ElapsedSeconds ?? 0.0;
        var rawPerSecond = elapsed > 0 ? raw / elapsed : 0.0;
        var rawPerPolynomial = polynomials > 0 ? (double)raw / polynomials : 0.0;
        var pairing = usable - full;
        var pairingRate = partial > 0 ? 100.0 * pairing / partial : 0.0;

        Console.WriteLine($"  [trial sieve]  {raw}/{target} raw relations sampled; {usable} usable relations observed for full target {Get(phase, "relations_needed")}");
        Console.WriteLine($"  [sample rate]  {rawPerSecond:F1} raw rel/s, {rawPerPolynomial:F3} raw rel/poly, {(polynomials > 0 ? elapsed / polynomials * 1000.0 : 0.0):F3} ms/poly");
        Console.WriteLine($"  [sample yield] { (polynomials > 0 ? (double)full / polynomials : 0.0):F3} full rel/poly; partial pairing contribution {pairing}/{partial} ({pairingRate:F2}%)");

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
