using System.Numerics;
using SIQS.Contracts.Numerics;

namespace Sieving;

/// <summary>A concrete SIQS polynomial <c>V(x) = A x^2 + 2B x + C</c> with the factor base positions of A's primes.</summary>
public readonly record struct PolynomialCandidate(BigInteger A, BigInteger B, BigInteger C, int[] APositions);

/// <summary>One polynomial in an A-family, including the Gray-code transition from the previous emitted member.</summary>
public readonly record struct PolynomialFamilyMember(
    PolynomialCandidate Polynomial,
    int? FlipIndex,
    int FlipDirection,
    int NormalizationCorrection);

/// <summary>All related polynomials for one A value, plus the B terms needed for incremental root updates.</summary>
public sealed record PolynomialFamilyBatch(
    BigInteger A,
    int[] APositions,
    BigInteger[] BTerms,
    IReadOnlyList<PolynomialFamilyMember> Members);

/// <summary>
/// Generates SIQS polynomials. <see cref="SelectAPositions"/> picks the factor base primes whose
/// product forms <c>A</c> (ordered by score); <see cref="Family"/> walks the <c>2^s</c> sign
/// vectors in binary-reflected Gray-code order to produce the <c>B</c> (and hence <c>C</c>) values
/// for a fixed <c>A</c>. See <c>Instructions/Sieving.md</c>.
/// </summary>
public static class PolynomialGenerator
{
    /// <summary>
    /// When the window's combination space is at most this size, every combination is enumerated
    /// and scored. Larger spaces switch to deterministic sampling, which allows a wide window.
    /// </summary>
    internal const int ExhaustiveCombinationLimit = 262_144;

    /// <summary>Upper bound on the number of candidate A sets produced by the sampling path.</summary>
    internal const int MaxSampledCandidates = 250_000;

    /// <summary>Returns candidate A prime-position sets ordered by ascending score (best first).</summary>
    public static IReadOnlyList<int[]> SelectAPositions(FactorBaseData fb, SievingParameters parameters)
    {
        var s = parameters.APrimeCount;
        if (s < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), "APrimeCount must be at least 1.");
        }

        // Eligible: odd factor base primes that do not divide the multiplier.
        var eligible = new List<int>();
        for (var i = 0; i < fb.Count; i++)
        {
            if (fb.Primes[i] == 2 || fb.Multiplier % fb.Primes[i] == 0)
            {
                continue;
            }

            eligible.Add(i);
        }

        if (eligible.Count < s)
        {
            return Array.Empty<int[]>();
        }

        var targetA = IntegerMath.Sqrt(2 * fb.ScaledN) / parameters.SieveHalfInterval;
        var targetLog = targetA > 0 ? BigInteger.Log(targetA) : 0.0;
        var idealPrime = targetA > 0 ? Math.Pow((double)targetA, 1.0 / s) : 0.0;

        // Window: the closest eligible primes to the ideal prime, by |p - ideal| then column.
        // The window must be wide: SIQS polynomials built from a narrow band of a-primes are so
        // strongly correlated that different (A, B) pairs rediscover the same smooth values
        // (mirrored duplicate relations) and the a-columns co-occur often enough to create
        // parity-column dependencies that starve Block Lanczos of dependencies.
        var window = eligible
            .OrderBy(i => Math.Abs(fb.Primes[i] - idealPrime))
            .ThenBy(i => fb.Columns[i])
            .Take(parameters.APrimeWindowSize)
            .OrderBy(i => fb.Columns[i])
            .ToArray();

        var candidates = ShouldEnumerate(window.Length, s, parameters)
            ? EnumerateAllCandidates(fb, window, s, targetLog)
            : SampleCandidates(fb, eligible, window, s, targetLog, parameters);

        return candidates
            .OrderBy(c => c.Score)
            .ThenBy(c => string.Join(',', c.Positions.Select(p => fb.Columns[p])))
            .Select(c => c.Positions)
            .ToArray();
    }

    /// <summary>
    /// Chooses between enumerating the whole combination space and deterministic sampling.
    /// Enumeration wins for small spaces, and also whenever the number of candidates we want to
    /// sample meets or exceeds what sampling can actually reach: the sampler draws <c>s-1</c> window
    /// positions and derives the final prime deterministically, so it can produce at most
    /// <c>C(window, s-1)</c> distinct A-sets. Asking for more makes rejection sampling thrash on
    /// duplicates until it hits its attempt cap — the source of the C46–C52 wall-clock cliff — while
    /// enumeration of the (only modestly larger) full space is both faster and higher quality.
    /// </summary>
    private static bool ShouldEnumerate(int windowLength, int s, SievingParameters parameters)
    {
        if (CombinationSpace(windowLength, s) <= ExhaustiveCombinationLimit)
        {
            return true;
        }

        var reachableBySampling = CombinationSpace(windowLength, s - 1);
        return SampledCandidateTarget(parameters, s) >= reachableBySampling;
    }

    /// <summary>Number of distinct A-sets the sampling path aims to produce.</summary>
    private static long SampledCandidateTarget(SievingParameters parameters, int s)
    {
        var familySize = 1L << Math.Max(0, s - 1);
        return Math.Clamp(parameters.PolynomialCount / familySize, 1, MaxSampledCandidates);
    }

    private static List<(double Score, int[] Positions)> EnumerateAllCandidates(
        FactorBaseData fb, int[] window, int s, double targetLog)
    {
        var candidates = new List<(double Score, int[] Positions)>();
        foreach (var combo in Combinations(window, s))
        {
            var a = BigInteger.One;
            foreach (var pos in combo)
            {
                a *= fb.Primes[pos];
            }

            candidates.Add((Math.Abs(BigInteger.Log(a) - targetLog), combo));
        }

        return candidates;
    }

    /// <summary>
    /// Deterministically samples distinct A sets from a wide window: s-1 positions are drawn
    /// pseudo-randomly from the window, and the final prime is chosen from the full eligible list
    /// so that log(A) lands closest to the target. Sampling keeps the candidate count bounded
    /// where exhaustive enumeration of the combination space is impossible.
    /// </summary>
    private static List<(double Score, int[] Positions)> SampleCandidates(
        FactorBaseData fb, List<int> eligible, int[] window, int s, double targetLog, SievingParameters parameters)
    {
        var targetCount = (int)SampledCandidateTarget(parameters, s);

        var windowLogs = new double[window.Length];
        for (var i = 0; i < window.Length; i++)
        {
            windowLogs[i] = Math.Log((double)fb.Primes[window[i]]);
        }

        var eligibleLogs = new double[eligible.Count];
        for (var i = 0; i < eligible.Count; i++)
        {
            eligibleLogs[i] = Math.Log((double)fb.Primes[eligible[i]]);
        }

        // Deterministic seed derived from ScaledN so runs are reproducible.
        var state = 0x9E3779B97F4A7C15UL;
        foreach (var b in fb.ScaledN.ToByteArray())
        {
            state = (state ^ b) * 0x100000001B3UL;
        }

        var candidates = new List<(double Score, int[] Positions)>(targetCount);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var picks = new int[s];
        var maxAttempts = (long)targetCount * 64;
        for (var attempt = 0L; attempt < maxAttempts && candidates.Count < targetCount; attempt++)
        {
            // Draw s-1 distinct window positions.
            var partialLog = 0.0;
            var chosen = 0;
            while (chosen < s - 1)
            {
                var idx = (int)(SplitMix64(ref state) % (ulong)window.Length);
                var duplicate = false;
                for (var i = 0; i < chosen; i++)
                {
                    if (picks[i] == window[idx])
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    continue;
                }

                picks[chosen++] = window[idx];
                partialLog += windowLogs[idx];
            }

            // Correction prime: the eligible prime bringing log(A) closest to the target.
            var lastIndex = FindBestCorrectionIndex(eligibleLogs, eligible, picks, s - 1, targetLog - partialLog);
            if (lastIndex < 0)
            {
                continue;
            }

            picks[s - 1] = eligible[lastIndex];
            var positions = picks.OrderBy(p => fb.Columns[p]).ToArray();
            var key = string.Join(',', positions);
            if (!seen.Add(key))
            {
                continue;
            }

            candidates.Add((Math.Abs(partialLog + eligibleLogs[lastIndex] - targetLog), positions));
        }

        return candidates;
    }

    /// <summary>
    /// Finds the index into <paramref name="eligible"/> whose log is closest to
    /// <paramref name="residualLog"/>, skipping positions already used in <paramref name="picks"/>.
    /// </summary>
    private static int FindBestCorrectionIndex(
        double[] eligibleLogs, List<int> eligible, int[] picks, int pickCount, double residualLog)
    {
        var lo = 0;
        var hi = eligibleLogs.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (eligibleLogs[mid] < residualLog)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        var best = -1;
        var bestDistance = double.MaxValue;
        foreach (var direction in stackalloc int[] { -1, 1 })
        {
            for (var i = direction < 0 ? lo : lo + 1; i >= 0 && i < eligibleLogs.Length; i += direction)
            {
                var distance = Math.Abs(eligibleLogs[i] - residualLog);
                if (distance >= bestDistance)
                {
                    break;
                }

                var used = false;
                for (var j = 0; j < pickCount; j++)
                {
                    if (picks[j] == eligible[i])
                    {
                        used = true;
                        break;
                    }
                }

                if (!used)
                {
                    best = i;
                    bestDistance = distance;
                    break;
                }
            }
        }

        return best;
    }

    private static long CombinationSpace(int windowSize, int choose)
    {
        if (choose < 0 || windowSize < choose)
        {
            return 0;
        }

        choose = Math.Min(choose, windowSize - choose);
        var result = 1L;
        for (var i = 1; i <= choose; i++)
        {
            var factor = windowSize - choose + i;
            if (result > long.MaxValue / factor)
            {
                return long.MaxValue;
            }

            result *= factor;
            result /= i;
        }

        return result;
    }

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Generates the polynomials for a fixed A in canonical Gray-code sign-vector order.</summary>
    public static IEnumerable<PolynomialCandidate> Family(FactorBaseData fb, int[] aPositions)
        => BuildFamily(fb, aPositions).Members.Select(m => m.Polynomial);

    /// <summary>Builds the fixed-A family in canonical Gray-code order, with transition metadata.</summary>
    public static PolynomialFamilyBatch BuildFamily(FactorBaseData fb, int[] aPositions)
    {
        foreach (var pos in aPositions)
        {
            if ((uint)pos >= (uint)fb.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(aPositions), $"A-prime position {pos} is outside the factor base.");
            }
        }

        var positions = aPositions.OrderBy(p => fb.Columns[p]).ToArray();
        var s = positions.Length;

        var a = BigInteger.One;
        foreach (var pos in positions)
        {
            var prime = fb.Primes[pos];
            if (fb.Multiplier % prime == 0)
            {
                throw new InvalidOperationException(
                    $"Selected A prime {prime} divides the multiplier {fb.Multiplier}; multiplier-prime factor base entries cannot be used in polynomial A.");
            }

            a *= fb.Primes[pos];
        }

        // B_i = (A / q_i) * gamma_i, where gamma_i = root1_i * inverse(A / q_i) mod q_i (reduced).
        var bi = new BigInteger[s];
        for (var i = 0; i < s; i++)
        {
            var q = fb.Primes[positions[i]];
            var aOverQ = a / q;
            var inv = IntegerMath.ModInverse((long)IntegerMath.Mod(aOverQ, q), q);
            var gamma = IntegerMath.Mod(fb.Root1[positions[i]] * inv, q);
            if (2 * gamma > q)
            {
                gamma = q - gamma;
            }

            bi[i] = aOverQ * gamma;
        }

        // The sieve interval is symmetric, so the sign vector eps and its negation produce
        // conjugate polynomials: V_B(x) == V_-B(-x), with T changing sign. Emitting both halves
        // floods the matrix with guaranteed-trivial two-row dependencies, so use one canonical
        // representative from each pair. In reflected Gray-code order, the first half has the
        // high bit clear and contains exactly one vector from every complement pair.
        var members = new List<PolynomialFamilyMember>(1 << Math.Max(0, s - 1));
        var total = 1 << Math.Max(0, s - 1);
        BigInteger? previousB = null;
        var previousGray = 0;
        for (var j = 0; j < total; j++)
        {
            var gray = j ^ (j >> 1);

            var signedSum = BigInteger.Zero;
            for (var i = 0; i < s; i++)
            {
                var bit = (gray >> i) & 1;
                signedSum += bit == 1 ? bi[i] : -bi[i];
            }

            var b = IntegerMath.Mod(signedSum, a);
            if (2 * b > a)
            {
                b -= a; // normalize to (-A/2, A/2]
            }

            var numerator = b * b - fb.ScaledN;
            if (numerator % a != 0)
            {
                // B is built from modular roots of N, so B² ≡ N (mod A) holds by construction.
                // Skipping a member here would leave previousGray stale and silently corrupt the
                // incremental flip metadata of the next member, so a violation must be fatal.
                throw new InvalidOperationException(
                    $"Polynomial family invariant violated: B² - N is not divisible by A (A = {a}, B = {b}).");
            }

            int? flipIndex = null;
            var flipDirection = 0;
            var normalizationCorrection = 0;
            if (previousB is { } prev)
            {
                var changed = previousGray ^ gray;
                flipIndex = BitOperations.TrailingZeroCount((uint)changed);
                flipDirection = ((gray >> flipIndex.Value) & 1) == 1 ? 1 : -1;
                var rawDelta = 2 * flipDirection * bi[flipIndex.Value];
                var correction = (b - prev - rawDelta) / a;
                normalizationCorrection = (int)correction;
            }

            members.Add(new PolynomialFamilyMember(
                new PolynomialCandidate(a, b, numerator / a, positions),
                flipIndex,
                flipDirection,
                normalizationCorrection));
            previousB = b;
            previousGray = gray;
        }

        return new PolynomialFamilyBatch(a, positions, bi, members);
    }

    private static IEnumerable<int[]> Combinations(int[] items, int choose)
    {
        var indices = new int[choose];
        for (var i = 0; i < choose; i++)
        {
            indices[i] = i;
        }

        while (true)
        {
            yield return indices.Select(i => items[i]).ToArray();

            var k = choose - 1;
            while (k >= 0 && indices[k] == items.Length - choose + k)
            {
                k--;
            }

            if (k < 0)
            {
                yield break;
            }

            indices[k]++;
            for (var i = k + 1; i < choose; i++)
            {
                indices[i] = indices[i - 1] + 1;
            }
        }
    }
}
