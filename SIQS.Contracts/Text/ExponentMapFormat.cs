using SIQS.Contracts;

namespace SIQS.Contracts.Text;

/// <summary>
/// Parses and writes exponent maps of the form <c>1:2 4:1 9:3</c>, mapping a column index to its
/// integer exponent. Writers emit columns in ascending order and omit zero exponents, so output
/// is deterministic. Also derives parity (odd-exponent) columns from a map.
/// </summary>
public static class ExponentMapFormat
{
    private static readonly char[] Separators = { ' ', '\t' };

    /// <summary>Parses an exponent map. A blank string yields an empty map.</summary>
    public static IReadOnlyDictionary<int, int> Parse(string s)
    {
        var map = new Dictionary<int, int>();
        foreach (var token in s.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = token.IndexOf(':');
            if (colon <= 0)
            {
                throw new FormatException($"Invalid exponent token '{token}'.");
            }

            var column = int.Parse(token[..colon]);
            var exponent = int.Parse(token[(colon + 1)..]);
            map[column] = exponent;
        }

        return map;
    }

    /// <summary>Writes a map as ascending <c>column:exponent</c> pairs, omitting zero exponents.</summary>
    public static string Write(IReadOnlyDictionary<int, int> map)
        => string.Join(' ', map
            .Where(kv => kv.Value != 0)
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}:{kv.Value}"));

    public static string Write(SparseExponentVector vector)
        => string.Join(' ', vector.Select(kv => $"{kv.Key}:{kv.Value}"));

    /// <summary>Returns the ascending list of columns whose exponent is odd.</summary>
    public static IReadOnlyList<int> ParityColumns(IReadOnlyDictionary<int, int> map)
        => map
            .Where(kv => (kv.Value & 1) == 1)
            .Select(kv => kv.Key)
            .OrderBy(c => c)
            .ToArray();

    public static ParityColumnSet ParityColumns(SparseExponentVector vector) => vector.DeriveParity();
}
