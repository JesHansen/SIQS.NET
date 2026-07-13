using System.Collections;

namespace SIQS.Contracts;

/// <summary>An owned sorted set of factor-base columns with odd exponent parity.</summary>
public sealed class ParityColumnSet : IEquatable<ParityColumnSet>, IReadOnlyList<int>
{
    private readonly int[] _columns;

    public ParityColumnSet(IReadOnlyList<int> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        _columns = columns.ToArray();
        ValidateStorage(_columns);
    }

    internal static ParityColumnSet FromOwned(int[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ValidateStorage(columns);
        return new ParityColumnSet(columns, takeOwnership: true);
    }

    private ParityColumnSet(int[] columns, bool takeOwnership)
        => _columns = takeOwnership ? columns : columns.ToArray();

    public int Count => _columns.Length;

    public int this[int index] => _columns[index];

    public ReadOnlySpan<int> Span => _columns;

    public bool Contains(int column) => Array.BinarySearch(_columns, column) >= 0;

    public Enumerator GetEnumerator() => new(_columns);

    IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(ParityColumnSet? other)
        => other is not null && Span.SequenceEqual(other.Span);

    public override bool Equals(object? obj) => obj is ParityColumnSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var column in _columns)
        {
            hash.Add(column);
        }

        return hash.ToHashCode();
    }

    private static void ValidateStorage(int[] columns)
    {
        for (var i = 0; i < columns.Length; i++)
        {
            if (columns[i] < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), columns[i], "Parity columns cannot be negative.");
            }

            if (i > 0 && columns[i - 1] >= columns[i])
            {
                throw new ArgumentException("Parity columns must be ascending and unique.");
            }
        }
    }

    public struct Enumerator : IEnumerator<int>
    {
        private readonly int[] _columns;
        private int _index;

        internal Enumerator(int[] columns)
        {
            _columns = columns;
            _index = -1;
        }

        public int Current => _columns[_index];

        object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _columns.Length;

        public void Reset() => _index = -1;

        public void Dispose() { }
    }
}
