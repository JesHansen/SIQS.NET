using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Contracts.Numerics;

namespace SIQS.Pipeline;

/// <summary>Structural artifact invariants plus bounded mathematical resume verification.</summary>
internal static class ArtifactInvariantValidator
{
    internal const int MathematicalRelationSampleSize = 32;

    public static void FactorBase(
        FactorBaseDocument document,
        ValidationResultBuilder errors,
        string artifact = "factor_base.txt")
    {
        var meta = document.Metadata;
        errors.ErrorIf(meta.Bound < 2, "invalid_factor_base", $"{artifact} bound must be at least 2.");
        errors.ErrorIf(!double.IsFinite(meta.LogScale) || meta.LogScale <= 0,
            "invalid_factor_base", $"{artifact} log_scale must be finite and positive.");

        var seenPrimes = new HashSet<long>();
        long previousPrime = 1;
        for (var i = 0; i < document.Entries.Count; i++)
        {
            var entry = document.Entries[i];
            var label = $"{artifact} entry {i}";
            errors.ErrorIf(entry.Index != i + 1, "invalid_factor_base",
                $"{label} has non-contiguous index {entry.Index}; expected {i + 1}.");
            errors.ErrorIf(entry.Prime <= previousPrime, "invalid_factor_base",
                $"{label} primes are not strictly ascending.");
            errors.ErrorIf(!seenPrimes.Add(entry.Prime), "invalid_factor_base",
                $"{label} repeats prime {entry.Prime}.");
            errors.ErrorIf(entry.Prime < 2 || entry.Prime > meta.Bound, "invalid_factor_base",
                $"{label} prime {entry.Prime} is outside [2, {meta.Bound}].");
            errors.ErrorIf(entry.Root1 < 0 || entry.Root1 >= entry.Prime ||
                entry.Root2 < 0 || entry.Root2 >= entry.Prime || entry.Root1 > entry.Root2,
                "invalid_factor_base", $"{label} roots are outside the canonical prime range.");
            errors.ErrorIf(entry.LogP is < 0 or > 255, "invalid_factor_base",
                $"{label} logp is outside [0, 255].");
            // The factor-base contract reserves (0, 0) for prime 2; the sieve handles parity
            // separately instead of storing its mathematical root.
            if (entry.Prime > 2)
            {
                var expected = IntegerMath.Mod(meta.ScaledN, entry.Prime);
                errors.ErrorIf(
                    IntegerMath.Mod((BigInteger)entry.Root1 * entry.Root1, entry.Prime) != expected ||
                    IntegerMath.Mod((BigInteger)entry.Root2 * entry.Root2, entry.Prime) != expected,
                    "invalid_factor_base", $"{label} roots do not square to scaled_n modulo the prime.");
            }

            previousPrime = entry.Prime;
        }
    }

    public static void RawRelations(
        IReadOnlyList<RawRelationRecord> records,
        string artifact,
        RelationKind expectedKind,
        RelationVerifier verifier,
        HashSet<string> allIds,
        ValidationResultBuilder errors)
    {
        for (var i = 0; i < records.Count; i++)
        {
            var relation = records[i];
            var label = $"{artifact} relation '{relation.RelationId}'";
            errors.ErrorIf(string.IsNullOrWhiteSpace(relation.RelationId), "invalid_relation",
                $"{artifact} contains an empty relation id.");
            errors.ErrorIf(!allIds.Add(relation.RelationId), "invalid_relation",
                $"{label} has a duplicate id across raw batches.");
            errors.ErrorIf(relation.Kind != expectedKind, "invalid_relation",
                $"{label} has kind {relation.Kind}; expected {expectedKind}.");
            CheckExponentParity(relation.FactorExponents, relation.ParityColumns, relation.Sign, label, errors);
        }

        foreach (var index in SampleIndexes(records.Count))
        {
            if (!verifier.TryVerify(records[index], out var error))
            {
                errors.Error("invalid_relation", $"{artifact}: {error}");
            }
        }
    }

    public static void FilteredRelations(
        FilteredRelationsDocument document,
        FactorBaseDocument factorBase,
        ValidationResultBuilder errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < document.Relations.Count; i++)
        {
            var relation = document.Relations[i];
            var expectedId = $"F{i:D8}";
            var label = $"relations_filtered.txt relation '{relation.RelationId}'";
            errors.ErrorIf(relation.RelationId != expectedId, "invalid_filtered_relation",
                $"{label} is out of order; expected '{expectedId}'.");
            errors.ErrorIf(!ids.Add(relation.RelationId), "invalid_filtered_relation",
                $"{label} has a duplicate id.");
            errors.ErrorIf(relation.Kind is not (RelationKind.Full or RelationKind.CombinedPartial),
                "invalid_filtered_relation", $"{label} has unsupported kind {relation.Kind}.");
            errors.ErrorIf(relation.SourceRelationIds.Count == 0,
                "invalid_filtered_relation", $"{label} has no source relation ids.");
            errors.ErrorIf(relation.Kind == RelationKind.Full && relation.LargePrimes.Count != 0,
                "invalid_filtered_relation", $"{label} is full but declares large primes.");
            errors.ErrorIf(relation.Kind == RelationKind.CombinedPartial && relation.LargePrimes.Count == 0,
                "invalid_filtered_relation", $"{label} is combined but has no squared large-prime roots.");
            CheckExponentParity(relation.Exponents, relation.ParityColumns, relation.Sign, label, errors);
        }

        var primes = factorBase.Entries.ToDictionary(entry => entry.Index, entry => (BigInteger)entry.Prime);
        foreach (var index in SampleIndexes(document.Relations.Count))
        {
            var relation = document.Relations[index];
            var product = BigInteger.One;
            var validColumns = true;
            foreach (var (column, exponent) in relation.Exponents)
            {
                if (column == 0)
                {
                    if ((exponent & 1) != 0) product = -product;
                }
                else if (primes.TryGetValue(column, out var prime))
                {
                    product *= BigInteger.Pow(prime, exponent);
                }
                else
                {
                    errors.Error("invalid_filtered_relation",
                        $"relations_filtered.txt relation '{relation.RelationId}' references unknown factor-base column {column}.");
                    validColumns = false;
                    break;
                }
            }

            if (!validColumns) continue;
            foreach (var largePrime in relation.LargePrimes)
            {
                errors.ErrorIf(largePrime <= factorBase.Metadata.Bound,
                    "invalid_filtered_relation",
                    $"relations_filtered.txt relation '{relation.RelationId}' has non-large prime {largePrime}.");
                product *= largePrime * largePrime;
            }

            errors.ErrorIf(
                BigInteger.ModPow(relation.T, 2, document.ScaledN) != IntegerMath.Mod(product, document.ScaledN),
                "invalid_filtered_relation",
                $"relations_filtered.txt relation '{relation.RelationId}' fails t^2 congruence verification.");
        }
    }

    public static void Matrix(
        IReadOnlyList<SparseMatrixRowRecord> matrix,
        MatrixMetadata meta,
        FilteredRelationsDocument? relations,
        ValidationResultBuilder errors)
    {
        for (var i = 0; i < matrix.Count; i++)
        {
            var row = matrix[i];
            errors.ErrorIf(row.RowId != i, "invalid_matrix",
                $"filtered_matrix.txt row position {i} has row_id {row.RowId}.");
            errors.ErrorIf(relations is not null && i < relations.Relations.Count &&
                row.RelationId != relations.Relations[i].RelationId,
                "invalid_matrix", $"filtered_matrix.txt row {row.RowId} relation id does not match relations_filtered.txt.");
            var previous = -1;
            foreach (var column in row.Columns)
            {
                errors.ErrorIf(column < 0 || column >= meta.ColumnCount, "invalid_matrix",
                    $"filtered_matrix.txt row {row.RowId} references column {column} outside [0, {meta.ColumnCount}).");
                errors.ErrorIf(column <= previous, "invalid_matrix",
                    $"filtered_matrix.txt row {row.RowId} columns are not strictly ascending and unique.");
                previous = column;
            }
        }
    }

    public static void Dependencies(
        DependenciesDocument document,
        IReadOnlyList<SparseMatrixRowRecord> matrix,
        ValidationResultBuilder errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < document.Dependencies.Count; i++)
        {
            var dependency = document.Dependencies[i];
            var label = $"dependencies.txt dependency {dependency.DependencyId}";
            errors.ErrorIf(dependency.DependencyId != i, "invalid_dependency",
                $"{label} is out of order; expected id {i}.");
            errors.ErrorIf(dependency.RowIds.Count == 0 || dependency.RowIds.Count != dependency.RelationIds.Count,
                "invalid_dependency", $"{label} has empty or mismatched row and relation id lists.");
            var previous = -1;
            var parity = new HashSet<int>();
            var signature = new List<int>();
            for (var rowIndex = 0; rowIndex < dependency.RowIds.Count; rowIndex++)
            {
                var rowId = dependency.RowIds[rowIndex];
                errors.ErrorIf(rowId <= previous || rowId < 0 || rowId >= matrix.Count,
                    "invalid_dependency", $"{label} row ids are not unique, ascending, and in range.");
                if (rowId >= 0 && rowId < matrix.Count)
                {
                    errors.ErrorIf(dependency.RelationIds[rowIndex] != matrix[rowId].RelationId,
                        "invalid_dependency", $"{label} relation id does not match matrix row {rowId}.");
                    foreach (var column in matrix[rowId].Columns)
                    {
                        if (!parity.Add(column)) parity.Remove(column);
                    }
                }

                signature.Add(rowId);
                previous = rowId;
            }

            errors.ErrorIf(parity.Count != 0, "invalid_dependency",
                $"{label} does not have zero matrix product.");
            errors.ErrorIf(!seen.Add(string.Join(',', signature)), "invalid_dependency",
                $"{label} duplicates an earlier dependency.");
        }
    }

    public static void Factors(FactorsDocument document, ValidationResultBuilder errors)
    {
        var isPrecheck = document.Results.Count == 1 && document.Results[0].DependencyId == "precheck";
        var expectedDependencyCount = isPrecheck ? 0 : document.Results.Count;
        errors.ErrorIf(document.DependencyCount != expectedDependencyCount,
            "invalid_factors", "factors.txt dependency_count does not match its result semantics.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in document.Results)
        {
            var label = $"factors.txt result '{result.DependencyId}'";
            errors.ErrorIf(!ids.Add(result.DependencyId), "invalid_factors", $"{label} has a duplicate id.");
            if (result.Status == FactorizationStatus.FactorFound)
            {
                errors.ErrorIf(result.Factor1 is not { } factor1 || result.Factor2 is not { } factor2 ||
                    factor1 <= 1 || factor2 <= 1 || factor1 >= document.TargetN || factor2 >= document.TargetN ||
                    factor1 * factor2 != document.TargetN,
                    "invalid_factors", $"{label} does not contain a proper factor pair for target_n.");
            }
            else
            {
                errors.ErrorIf(result.Factor1 is not null || result.Factor2 is not null,
                    "invalid_factors", $"{label} has factors for non-factor status {result.Status}.");
            }

            errors.ErrorIf(result.Status == FactorizationStatus.Invalid && string.IsNullOrWhiteSpace(result.Reason),
                "invalid_factors", $"{label} is invalid without a reason.");
        }
    }

    private static void CheckExponentParity(
        SparseExponentVector exponents,
        IReadOnlyList<int> parityColumns,
        int sign,
        string label,
        ValidationResultBuilder errors)
    {
        errors.ErrorIf(sign is not (-1 or 1), "invalid_relation", $"{label} sign must be -1 or 1.");
        errors.ErrorIf(exponents.Any(pair => pair.Key < 0 || pair.Value <= 0),
            "invalid_relation", $"{label} has a negative column or non-positive exponent.");
        var expected = exponents.Where(pair => (pair.Value & 1) != 0).Select(pair => pair.Key).Order().ToArray();
        errors.ErrorIf(!expected.SequenceEqual(parityColumns), "invalid_relation",
            $"{label} parity columns do not match odd exponents.");
        errors.ErrorIf(sign != (exponents.GetValueOrDefault(0) % 2 == 0 ? 1 : -1),
            "invalid_relation", $"{label} sign disagrees with column-zero parity.");
    }

    private static IEnumerable<int> SampleIndexes(int count)
    {
        if (count <= MathematicalRelationSampleSize)
        {
            return Enumerable.Range(0, count);
        }

        return Enumerable.Range(0, MathematicalRelationSampleSize)
            .Select(i => checked((int)((long)i * (count - 1) / (MathematicalRelationSampleSize - 1))));
    }
}
