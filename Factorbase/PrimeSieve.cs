namespace Factorbase;

/// <summary>Deterministic Sieve of Eratosthenes producing ascending primes up to a bound.</summary>
public static class PrimeSieve
{
    /// <summary>Returns all primes <c>p</c> with <c>2 &lt;= p &lt;= bound</c> in ascending order.</summary>
    public static IReadOnlyList<long> PrimesUpTo(long bound, CancellationToken cancellationToken = default)
    {
        if (bound < 2)
        {
            return Array.Empty<long>();
        }

        var composite = new bool[bound + 1];
        var primes = new List<long>();

        for (long i = 2; i <= bound; i++)
        {
            if ((i & 0x3fff) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (composite[i])
            {
                continue;
            }

            primes.Add(i);
            for (var j = i * i; j <= bound; j += i)
            {
                composite[j] = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return primes;
    }
}
