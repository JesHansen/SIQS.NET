using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Factorbase;

/// <summary>Builds factor-base entries and their shared metadata from validated inputs.</summary>
internal static class FactorBaseDocumentBuilder
{
    public static FactorBaseGenerationResult Build(BigInteger targetN, BigInteger multiplier, BigInteger scaledN, long bound)
    {
        var logScale = 255.0 / Math.Log(bound);
        var entries = new List<FactorBaseEntry>
        {
            new(1, 2, 0, 0, ScaleLog(logScale, 2)),
        };

        foreach (var prime in PrimeSieve.PrimesUpTo(bound).Where(prime => prime != 2))
        {
            if (scaledN % prime == 0)
            {
                if (targetN % prime == 0)
                {
                    return new FactorBaseGenerationResult(null, EarlyFactor.Create(targetN, prime, "small_prime_factor"));
                }

                entries.Add(new(entries.Count + 1, prime, 0, 0, ScaleLog(logScale, prime)));
                continue;
            }

            if (NumberTheory.Legendre(scaledN, prime) != 1)
            {
                continue;
            }

            var root = NumberTheory.TonelliShanks(scaledN, prime);
            entries.Add(new(entries.Count + 1, prime, Math.Min(root, prime - root), Math.Max(root, prime - root), ScaleLog(logScale, prime)));
        }

        return new FactorBaseGenerationResult(
            new FactorBaseDocument(new(targetN, multiplier, scaledN, bound, logScale), entries),
            null);
    }

    private static int ScaleLog(double logScale, long prime)
        => (int)Math.Round(logScale * Math.Log(prime), MidpointRounding.AwayFromZero);
}
