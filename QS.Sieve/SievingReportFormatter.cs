using SIQS.Contracts;

namespace QsSieve;

internal static class SievingReportFormatter
{
    public static void Write(SievingCommandResult result)
    {
        var counters = result.Counters;
        var parameters = result.Parameters;
        Console.WriteLine($"  [sieving]     full={counters.FullRelations}, partials={counters.Partials}, raw={counters.RawRelations}, usable={counters.UsableRelations}, polynomials={counters.Polynomials}/{parameters.PolynomialCount}");
        if (parameters.EnableTwoLargePrimes)
        {
            Console.WriteLine($"  [large primes]1LP={counters.OneLargePrimePartials}, 2LP={counters.TwoLargePrimePartials}, 2LP split={counters.TwoLargePrimeSplitSuccesses}/{counters.TwoLargePrimeSplitAttempts}");
            Console.WriteLine($"  [lp2 bounds]  relation={parameters.LargePrime2Bound}, threshold={parameters.LargePrime2ThresholdBound}");
        }

        if (counters.TrialRawRelationTarget is not { } target || counters.RawRelations <= 0)
        {
            return;
        }

        var seconds = result.Elapsed.TotalSeconds;
        var rawPerSecond = seconds > 0 ? counters.RawRelations / seconds : 0.0;
        var rawPerPolynomial = counters.Polynomials > 0 ? (double)counters.RawRelations / counters.Polynomials : 0.0;
        var usefulFull = Math.Max(0, counters.FullRelations - counters.ZeroParityFullRelations);
        var cycleContribution = Math.Max(0, counters.UsableRelations - usefulFull);
        var cycleRate = counters.Partials > 0 ? 100.0 * cycleContribution / counters.Partials : 0.0;
        Console.WriteLine($"  [trial sieve]  stopped after {counters.RawRelations}/{target} raw relations; {counters.UsableRelations} usable so far versus normal target {counters.RelationTarget}.");
        Console.WriteLine("  [trial meaning]the requested percent sizes this raw-relation stop budget; it is not a percent of polynomials, elapsed time, or total sieve work.");
        Console.WriteLine($"  [sample rate]  {rawPerSecond:F1} raw rel/s, {rawPerPolynomial:F3} raw rel/poly, {(counters.Polynomials > 0 ? seconds / counters.Polynomials * 1000 : 0):F3} ms/poly.");
        Console.WriteLine($"  [sample yield] {(counters.Polynomials > 0 ? (double)usefulFull / counters.Polynomials : 0):F3} useful full rel/poly; closed-cycle contribution {cycleContribution}/{counters.Partials} partials ({cycleRate:F2}%).");
    }
}
