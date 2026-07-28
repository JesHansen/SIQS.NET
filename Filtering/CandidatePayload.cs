using System.Numerics;
using System.Text;
using SIQS.Contracts;

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
