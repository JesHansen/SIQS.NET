using System.Diagnostics;
using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Numerics;
using SIQS.Contracts.Text;

namespace Sieving;

/// <summary>Builds a raw relation by trial-dividing one promising sieve value.</summary>
internal static class RelationBuilder
{
    private static readonly BigInteger ULongMax = ulong.MaxValue;

    internal static RawRelationRecord? Build(
        FactorBaseData fb, PolynomialCandidate poly, bool[] isAPrime, SievingParameters parameters,
        long x, BigInteger value, int sieveIndex, int[] root1Residues, int[] root2Residues,
        List<int>? knownPrimeHits, int directTrialLimit, PolynomialSieveWorker worker)
    {
        var preStart = Stopwatch.GetTimestamp();
        var sign = value.Sign < 0 ? -1 : 1;
        var exponents = worker.ExponentScratch;
        exponents.Clear();
        if (sign < 0) exponents[0] = 1;

        var remainder = BigInteger.Abs(value);
        ulong smallRemainder = 0;
        var fitsInUlong = remainder <= ULongMax;
        if (fitsInUlong) smallRemainder = (ulong)remainder;
        worker.Metrics.Ticks.TrialDivPre += Stopwatch.GetTimestamp() - preStart;

        for (var primeIndex = 0; primeIndex < directTrialLimit; primeIndex++)
        {
            if (!HitsRoot(fb, primeIndex, sieveIndex, root1Residues, root2Residues)) continue;
            DivideKnownHit(fb, primeIndex, exponents, ref remainder, ref smallRemainder, ref fitsInUlong);
            if (fitsInUlong ? smallRemainder == 1 : remainder.IsOne) break;
        }

        if (knownPrimeHits is not null && (fitsInUlong ? smallRemainder != 1 : !remainder.IsOne))
        {
            foreach (var primeIndex in knownPrimeHits)
            {
                DivideKnownHit(fb, primeIndex, exponents, ref remainder, ref smallRemainder, ref fitsInUlong);
                if (fitsInUlong ? smallRemainder == 1 : remainder.IsOne) break;
            }
        }

        var postStart = Stopwatch.GetTimestamp();
        DivideSelectedAPrimeFactors(fb, poly.APositions, exponents, ref remainder, ref smallRemainder, ref fitsInUlong);
        foreach (var position in poly.APositions)
        {
            var column = fb.Columns[position];
            exponents[column] = exponents.GetValueOrDefault(column) + 1;
        }

        var checkStart = Stopwatch.GetTimestamp();
        worker.Metrics.Ticks.TrialDivPostAPos += checkStart - postStart;
        if (!TryClassifyRemainder(fb, parameters, worker, remainder, smallRemainder, fitsInUlong, out var kind, out var largePrime, out var largePrimes))
        {
            var discard = Stopwatch.GetTimestamp();
            worker.Metrics.Ticks.TrialDivPostCheck += discard - checkStart;
            worker.Metrics.Ticks.TrialDivPost += discard - postStart;
            return null;
        }

        var parityStart = Stopwatch.GetTimestamp();
        worker.Metrics.Ticks.TrialDivPostCheck += parityStart - checkStart;
        var relationExponents = new Dictionary<int, int>(exponents);
        var parity = ExponentMapFormat.ParityColumns(relationExponents);
        var t = IntegerMath.Mod(poly.A * x + poly.B, fb.ScaledN);
        var result = new RawRelationRecord("TEMP", kind, "TEMP", poly.A, poly.B, poly.C, x, t, sign, relationExponents, parity, largePrime)
        {
            LargePrimes = largePrimes,
        };
        var end = Stopwatch.GetTimestamp();
        worker.Metrics.Ticks.TrialDivPostParity += end - parityStart;
        worker.Metrics.Ticks.TrialDivPost += end - postStart;
        return result;
    }

    private static bool HitsRoot(FactorBaseData fb, int primeIndex, int sieveIndex, int[] root1Residues, int[] root2Residues)
    {
        if (primeIndex == 0) return (sieveIndex & 1) == root1Residues[0];
        var inverse = fb.PrimeInverses[primeIndex];
        var threshold = fb.PrimeDivThresholds[primeIndex];
        var firstDifference = sieveIndex - root1Residues[primeIndex];
        if (firstDifference >= 0 && (ulong)firstDifference * inverse <= threshold) return true;
        var secondRoot = root2Residues[primeIndex];
        var secondDifference = sieveIndex - secondRoot;
        return secondRoot >= 0 && secondDifference >= 0 && (ulong)secondDifference * inverse <= threshold;
    }

    private static bool TryClassifyRemainder(
        FactorBaseData fb, SievingParameters parameters, PolynomialSieveWorker worker,
        BigInteger remainder, ulong smallRemainder, bool fitsInUlong,
        out RelationKind kind, out BigInteger? largePrime, out BigInteger[] largePrimes)
    {
        kind = RelationKind.Full;
        largePrime = null;
        largePrimes = [];
        if (fitsInUlong && smallRemainder == 1 || !fitsInUlong && remainder.IsOne) return true;

        if (fitsInUlong)
        {
            if (smallRemainder <= (ulong)parameters.LargePrimeBound && smallRemainder > (ulong)fb.Bound && CofactorPrimality64.IsPrime(smallRemainder))
            {
                kind = RelationKind.Partial;
                largePrime = new BigInteger(smallRemainder);
                largePrimes = [largePrime.Value];
                return true;
            }
            if (!parameters.EnableTwoLargePrimes) return false;
            worker.Metrics.TwoLargePrime.SplitAttempts++;
            TwoLargePrimeSplitter.RecordResidual(worker, smallRemainder);
            if (!TwoLargePrimeSplitter.TrySplit64(smallRemainder, (ulong)fb.Bound, (ulong)parameters.LargePrime2Bound, parameters.CofactorSplitter, worker, out var q1, out var q2)) return false;
            worker.Metrics.TwoLargePrime.SplitSuccesses++;
            kind = RelationKind.Partial;
            largePrimes = q1 <= q2 ? [q1, q2] : [q2, q1];
            return true;
        }

        if (remainder <= parameters.LargePrimeBound && remainder > fb.Bound && Primality.IsProbablePrime(remainder))
        {
            kind = RelationKind.Partial;
            largePrime = remainder;
            largePrimes = [remainder];
            return true;
        }
        if (!parameters.EnableTwoLargePrimes) return false;
        worker.Metrics.TwoLargePrime.SplitAttempts++;
        TwoLargePrimeSplitter.RecordResidual(worker, remainder);
        if (!TwoLargePrimeSplitter.TrySplit(remainder, fb.Bound, parameters.LargePrime2Bound, parameters.CofactorSplitter, worker, out var first, out var second)) return false;
        worker.Metrics.TwoLargePrime.SplitSuccesses++;
        kind = RelationKind.Partial;
        largePrimes = first <= second ? [first, second] : [second, first];
        return true;
    }

    internal static void DivideKnownHit(FactorBaseData fb, int primeIndex, Dictionary<int, int> exponents, ref BigInteger remainder, ref ulong smallRemainder, ref bool fitsInUlong)
    {
        if (fitsInUlong)
        {
            if (primeIndex == 0)
            {
                var power = 0;
                do { smallRemainder >>= 1; power++; } while ((smallRemainder & 1) == 0);
                exponents[fb.Columns[0]] = power;
                return;
            }
            var inverse = fb.PrimeInverses[primeIndex];
            var threshold = fb.PrimeDivThresholds[primeIndex];
            var quotient = smallRemainder * inverse;
            var exponent = 0;
            do { smallRemainder = quotient; exponent++; quotient = smallRemainder * inverse; } while (quotient <= threshold);
            exponents[fb.Columns[primeIndex]] = exponent;
            return;
        }
        var prime = fb.Primes[primeIndex];
        var bigExponent = 0;
        while (remainder % prime == 0) { remainder /= prime; bigExponent++; }
        if (bigExponent > 0) exponents[fb.Columns[primeIndex]] = bigExponent;
        if (remainder <= ULongMax) { smallRemainder = (ulong)remainder; fitsInUlong = true; }
    }

    internal static void DivideSelectedAPrimeFactors(FactorBaseData fb, IReadOnlyList<int> positions, Dictionary<int, int> exponents, ref BigInteger remainder, ref ulong smallRemainder, ref bool fitsInUlong)
    {
        foreach (var primeIndex in positions)
        {
            var prime = fb.Primes[primeIndex];
            var exponent = 0;
            if (fitsInUlong)
            {
                while (smallRemainder != 0 && smallRemainder % (ulong)prime == 0) { smallRemainder /= (ulong)prime; exponent++; }
                remainder = smallRemainder;
            }
            else
            {
                while (remainder % prime == 0) { remainder /= prime; exponent++; }
                if (remainder <= ULongMax) { smallRemainder = (ulong)remainder; fitsInUlong = true; }
            }
            if (exponent > 0)
            {
                var column = fb.Columns[primeIndex];
                exponents[column] = exponents.GetValueOrDefault(column) + exponent;
            }
        }
    }
}
