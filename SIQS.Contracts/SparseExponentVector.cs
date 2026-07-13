using System.Collections;

namespace SIQS.Contracts;

/// <summary>
/// An owned sparse exponent vector. Columns are non-negative, ascending, and unique; zero
/// exponents are omitted. The compact arrays are exposed only as read-only spans.
/// </summary>
public sealed class SparseExponentVector : IEquatable<SparseExponentVector>, IEnumerable<KeyValuePair<int, int>>
{
    private readonly int[] _columns;
    private readonly int[] _values;

    public SparseExponentVector(IReadOnlyDictionary<int, int> exponents)
    {
        ArgumentNullException.ThrowIfNull(exponents);

        var nonZero = exponents.Where(pair => pair.Value != 0).OrderBy(pair => pair.Key).ToArray();
        _columns = new int[nonZero.Length];
        _values = new int[nonZero.Length];
        for (var i = 0; i < nonZero.Length; i++)
        {
            ValidateColumn(nonZero[i].Key);
            _columns[i] = nonZero[i].Key;
            _values[i] = nonZero[i].Value;
        }
    }

    /// <summary>Creates a defensive copy of already compact column/value sequences.</summary>
    public SparseExponentVector(IReadOnlyList<int> columns, IReadOnlyList<int> values)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(values);
        if (columns.Count != values.Count)
        {
            throw new ArgumentException("Exponent columns and values must have the same length.");
        }

        _columns = columns.ToArray();
        _values = values.ToArray();
        ValidateStorage(_columns, _values);
    }

    internal static SparseExponentVector FromOwned(int[] columns, int[] values)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(values);
        ValidateStorage(columns, values);
        return new SparseExponentVector(columns, values, takeOwnership: true);
    }

    private SparseExponentVector(int[] columns, int[] values, bool takeOwnership)
    {
        _columns = takeOwnership ? columns : columns.ToArray();
        _values = takeOwnership ? values : values.ToArray();
    }

    public int Count => _columns.Length;

    public ReadOnlySpan<int> ColumnsSpan => _columns;

    public ReadOnlySpan<int> ValuesSpan => _values;

    public bool TryGetExponent(int column, out int exponent)
    {
        var index = Array.BinarySearch(_columns, column);
        if (index >= 0)
        {
            exponent = _values[index];
            return true;
        }

        exponent = 0;
        return false;
    }

    /// <summary>Dictionary-shaped alias for callers that only need a lookup at a boundary.</summary>
    public bool TryGetValue(int column, out int exponent) => TryGetExponent(column, out exponent);

    public int GetValueOrDefault(int column) => TryGetExponent(column, out var value) ? value : 0;

    public bool ContainsKey(int column) => Array.BinarySearch(_columns, column) >= 0;

    public Dictionary<int, int> ToDictionary()
    {
        var result = new Dictionary<int, int>(_columns.Length);
        for (var i = 0; i < _columns.Length; i++)
        {
            result.Add(_columns[i], _values[i]);
        }

        return result;
    }

    public ParityColumnSet DeriveParity() => ParityColumnSet.FromOwned(
        _columns.Where((_, index) => (_values[index] & 1) != 0).ToArray());

    public Enumerator GetEnumerator() => new(_columns, _values);

    IEnumerator<KeyValuePair<int, int>> IEnumerable<KeyValuePair<int, int>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(SparseExponentVector? other)
        => other is not null
            && ColumnsSpan.SequenceEqual(other.ColumnsSpan)
            && ValuesSpan.SequenceEqual(other.ValuesSpan);

    public override bool Equals(object? obj) => obj is SparseExponentVector other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < _columns.Length; i++)
        {
            hash.Add(_columns[i]);
            hash.Add(_values[i]);
        }

        return hash.ToHashCode();
    }

    private static void ValidateStorage(int[] columns, int[] values)
    {
        for (var i = 0; i < columns.Length; i++)
        {
            ValidateColumn(columns[i]);
            if (values[i] == 0)
            {
                throw new ArgumentException("Sparse exponent vectors cannot contain zero exponents.");
            }

            if (i > 0 && columns[i - 1] >= columns[i])
            {
                throw new ArgumentException("Sparse exponent columns must be ascending and unique.");
            }
        }
    }

    private static void ValidateColumn(int column)
    {
        if (column < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Exponent columns cannot be negative.");
        }
    }

    public struct Enumerator : IEnumerator<KeyValuePair<int, int>>
    {
        private readonly int[] _columns;
        private readonly int[] _values;
        private int _index;

        internal Enumerator(int[] columns, int[] values)
        {
            _columns = columns;
            _values = values;
            _index = -1;
        }

        public KeyValuePair<int, int> Current => new(_columns[_index], _values[_index]);

        object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _columns.Length;

        public void Reset() => _index = -1;

        public void Dispose() { }
    }
}
