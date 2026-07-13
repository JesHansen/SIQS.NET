using System.Numerics;

namespace SIQS.Contracts.Text;

/// <summary>
/// Parses and writes space-separated integer lists (e.g. <c>parity_columns</c> and dependency
/// <c>row_ids</c>). The writer preserves the caller's order; callers that need ascending order
/// must sort first. A blank string round-trips to an empty list.
/// </summary>
public static class IntegerListFormat
{
    private static readonly char[] Separators = { ' ', '\t' };

    /// <summary>Parses a space-separated list of 32-bit integers.</summary>
    public static IReadOnlyList<int> ParseInts(string s)
        => Tokens(s).Select(int.Parse).ToArray();

    /// <summary>Parses a space-separated list of 64-bit integers.</summary>
    public static IReadOnlyList<long> ParseLongs(string s)
        => Tokens(s).Select(long.Parse).ToArray();

    /// <summary>Parses a space-separated list of arbitrary-precision integers.</summary>
    public static IReadOnlyList<BigInteger> ParseBigIntegers(string s)
        => Tokens(s).Select(BigInteger.Parse).ToArray();

    /// <summary>Writes a list of integers joined by single spaces.</summary>
    public static string WriteInts(IEnumerable<int> values) => string.Join(' ', values);

    /// <summary>Writes a list of arbitrary-precision integers joined by single spaces.</summary>
    public static string WriteBigIntegers(IEnumerable<BigInteger> values) => string.Join(' ', values);

    private static string[] Tokens(string s)
        => s.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
