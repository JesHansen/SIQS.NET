using SIQS.Contracts;

namespace Filtering;

/// <summary>Structural checks shared by full and partial relation intake.</summary>
internal static class RelationValidation
{
    public static void ValidateColumns(IReadOnlyDictionary<int, int> exponents, int factorBaseCount)
    {
        foreach (var col in exponents.Keys)
        {
            if (col < 0 || col > factorBaseCount)
            {
                throw new FormatException($"Relation references factor base column {col} outside [0, {factorBaseCount}].");
            }
        }
    }

    public static void ValidateColumns(SparseExponentVector exponents, int factorBaseCount)
    {
        foreach (var col in exponents.ColumnsSpan)
        {
            if (col < 0 || col > factorBaseCount)
            {
                throw new FormatException($"Relation references factor base column {col} outside [0, {factorBaseCount}].");
            }
        }
    }

    public static void ValidateDeclaredParity(RawRelationRecord relation)
    {
        var expected = relation.FactorExponents.DeriveParity();
        if (!expected.Equals(relation.ParityColumns))
        {
            throw new FormatException(
                $"Relation '{relation.RelationId}' declared parity does not match its exponents.");
        }
    }
}
