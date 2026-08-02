using System.Numerics;
using System.Text;
using SIQS.Contracts;
using SIQS.Contracts.Numerics;

namespace Filtering;

/// <summary>
/// The heavy arithmetic half of a <see cref="Candidate"/>: everything the final output build needs but
/// that duplicate removal, singleton pruning, and row trimming never touch. Splitting it out lets the
/// engine keep only the light structural half (parity, order key, fingerprint) resident while the
/// payload is optionally spilled to disk under <c>--filter-spill-dir</c>.
/// </summary>
internal readonly record struct CandidatePayload(
    string[] SourceIds,
    BigInteger T,
    SparseExponentVector Exponents,
    BigInteger[] LargePrimes);

/// <summary>Combines the arithmetic provenance of two rows eliminated through a weight-2 column.</summary>
internal static class CandidatePayloadMerger
{
    public static CandidatePayload Merge(CandidatePayload left, CandidatePayload right, BigInteger scaledN)
        => new(
            MergeSorted(left.SourceIds, right.SourceIds, StringComparer.Ordinal),
            IntegerMath.Mod(left.T * right.T, scaledN),
            Sum(left.Exponents, right.Exponents),
            MergeSorted(left.LargePrimes, right.LargePrimes, Comparer<BigInteger>.Default));

    private static SparseExponentVector Sum(SparseExponentVector left, SparseExponentVector right)
    {
        var leftColumns = left.ColumnsSpan;
        var leftValues = left.ValuesSpan;
        var rightColumns = right.ColumnsSpan;
        var rightValues = right.ValuesSpan;
        var columns = new int[leftColumns.Length + rightColumns.Length];
        var values = new int[columns.Length];
        var leftIndex = 0;
        var rightIndex = 0;
        var count = 0;

        while (leftIndex < leftColumns.Length || rightIndex < rightColumns.Length)
        {
            if (rightIndex >= rightColumns.Length
                || (leftIndex < leftColumns.Length && leftColumns[leftIndex] < rightColumns[rightIndex]))
            {
                columns[count] = leftColumns[leftIndex];
                values[count++] = leftValues[leftIndex++];
            }
            else if (leftIndex >= leftColumns.Length || rightColumns[rightIndex] < leftColumns[leftIndex])
            {
                columns[count] = rightColumns[rightIndex];
                values[count++] = rightValues[rightIndex++];
            }
            else
            {
                columns[count] = leftColumns[leftIndex];
                values[count++] = checked(leftValues[leftIndex++] + rightValues[rightIndex++]);
            }
        }

        return SparseExponentVector.FromOwned(columns[..count], values[..count]);
    }

    private static T[] MergeSorted<T>(T[] left, T[] right, IComparer<T> comparer)
    {
        var result = new T[left.Length + right.Length];
        var leftIndex = 0;
        var rightIndex = 0;
        var resultIndex = 0;
        while (leftIndex < left.Length || rightIndex < right.Length)
        {
            if (rightIndex >= right.Length
                || (leftIndex < left.Length && comparer.Compare(left[leftIndex], right[rightIndex]) <= 0))
            {
                result[resultIndex++] = left[leftIndex++];
            }
            else
            {
                result[resultIndex++] = right[rightIndex++];
            }
        }

        return result;
    }
}

/// <summary>
/// Length-prefixed binary serialization for a <see cref="CandidatePayload"/>. The format is private to
/// the spill store: it is written and re-read within a single filtering run, never persisted across
/// runs, so it carries no version header.
/// </summary>
internal static class CandidatePayloadCodec
{
    public static void Write(BinaryWriter writer, CandidatePayload payload)
    {
        writer.Write(payload.SourceIds.Length);
        foreach (var id in payload.SourceIds)
        {
            writer.Write(id);
        }

        WriteBigInteger(writer, payload.T);

        var columns = payload.Exponents.ColumnsSpan;
        var values = payload.Exponents.ValuesSpan;
        writer.Write(columns.Length);
        foreach (var column in columns)
        {
            writer.Write(column);
        }

        foreach (var value in values)
        {
            writer.Write(value);
        }

        writer.Write(payload.LargePrimes.Length);
        foreach (var q in payload.LargePrimes)
        {
            WriteBigInteger(writer, q);
        }
    }

    public static CandidatePayload Read(BinaryReader reader)
    {
        var sourceIdCount = reader.ReadInt32();
        var sourceIds = new string[sourceIdCount];
        for (var i = 0; i < sourceIdCount; i++)
        {
            sourceIds[i] = reader.ReadString();
        }

        var t = ReadBigInteger(reader);

        var length = reader.ReadInt32();
        var columns = new int[length];
        for (var i = 0; i < length; i++)
        {
            columns[i] = reader.ReadInt32();
        }

        var values = new int[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = reader.ReadInt32();
        }

        var largePrimeCount = reader.ReadInt32();
        var largePrimes = new BigInteger[largePrimeCount];
        for (var i = 0; i < largePrimeCount; i++)
        {
            largePrimes[i] = ReadBigInteger(reader);
        }

        return new CandidatePayload(sourceIds, t, SparseExponentVector.FromOwned(columns, values), largePrimes);
    }

    private static void WriteBigInteger(BinaryWriter writer, BigInteger value)
    {
        var bytes = value.ToByteArray();
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static BigInteger ReadBigInteger(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        return new BigInteger(reader.ReadBytes(length));
    }
}
