namespace SIQS.Contracts;

/// <summary>
/// Helpers for content-based equality of the collection members carried by the shared records,
/// so value semantics survive round-tripping through the text file serializers.
/// </summary>
internal static class StructuralEquality
{
    public static bool SequenceEqual<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool DictionaryEqual<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> a,
        IReadOnlyDictionary<TKey, TValue> b)
        where TKey : notnull
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || !EqualityComparer<TValue>.Default.Equals(value, other))
            {
                return false;
            }
        }

        return true;
    }

    public static int CombineCounts(params int[] counts)
    {
        var hash = new HashCode();
        foreach (var c in counts)
        {
            hash.Add(c);
        }

        return hash.ToHashCode();
    }
}
