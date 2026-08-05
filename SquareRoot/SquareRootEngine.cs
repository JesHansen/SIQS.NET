using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Contracts.Numerics;

namespace SquareRoot;

/// <summary>Options for square-root extraction.</summary>
public sealed record SquareRootOptions(bool ContinueAfterFactor = false);

/// <summary>The result of square-root extraction: the factors document plus the first factor pair found.</summary>
public sealed record SquareRootResult(
    FactorsDocument Factors,
    BigInteger? Factor1,
    BigInteger? Factor2);

/// <summary>
/// Reconstructs the congruence of squares for each dependency and extracts non-trivial factors of
/// <c>TargetN</c> via <c>gcd(|X - Y|, N)</c> and <c>gcd(X + Y, N)</c>.
/// </summary>
public static class SquareRootEngine
{
    public static SquareRootResult Run(
        FactorBaseDocument factorBase,
        FilteredRelationsDocument relations,
        DependenciesDocument dependencies,
        SquareRootOptions? options = null,
        IProgress<SiqsProgressEvent>? progress = null)
    {
        options ??= new SquareRootOptions();
        var targetN = factorBase.Metadata.TargetN;
        var primeByColumn = factorBase.Entries.ToDictionary(e => e.Index, e => (BigInteger)e.Prime);
        var relationById = relations.Relations.ToDictionary(r => r.RelationId, StringComparer.Ordinal);

        var results = new List<FactorResultRecord>();
        BigInteger? factor1 = null, factor2 = null;

        foreach (var dependency in dependencies.Dependencies)
        {
            var row = ProcessDependency(dependency, targetN, primeByColumn, relationById);
            results.Add(row);

            if (row.Status == FactorizationStatus.FactorFound)
            {
                factor1 ??= row.Factor1;
                factor2 ??= row.Factor2;
                Report(progress, "factor found", results.Count, found: true);

                if (!options.ContinueAfterFactor)
                {
                    break;
                }
            }
        }

        var doc = new FactorsDocument(
            targetN, factorBase.Metadata.Multiplier, factorBase.Metadata.ScaledN,
            DependencyCount: results.Count, Results: results);

        return new SquareRootResult(doc, factor1, factor2);
    }

    private static FactorResultRecord ProcessDependency(
        DependencyRecord dependency,
        BigInteger targetN,
        IReadOnlyDictionary<int, BigInteger> primeByColumn,
        IReadOnlyDictionary<string, FilteredRelationRecord> relationById)
    {
        var id = dependency.DependencyId.ToString();

        if (dependency.RowIds.Count != dependency.RelationIds.Count)
        {
            return Invalid(id, "row_relation_count_mismatch");
        }

        if (!DependencyRelationResolver.TryResolve(dependency.RelationIds, relationById, out var selected))
        {
            return Invalid(id, "missing_relation");
        }

        if (!SquareCongruenceBuilder.TryBuild(selected, targetN, primeByColumn, out var congruence, out var invalidReason))
        {
            return Invalid(id, invalidReason!);
        }

        var (g1, g2, factor1, factor2) = ExtractFactor(congruence.X, congruence.Y, targetN);
        if (factor1 is null)
        {
            return new FactorResultRecord(id, FactorizationStatus.Trivial, g1, g2, null, null, "x_equals_y_or_negative_y");
        }

        return FactorResultRecord.FactorFound(id, targetN, g1, g2, factor1.Value, factor2!.Value);
    }

    /// <summary>
    /// Computes <c>gcd(|X - Y|, N)</c> and <c>gcd(X + Y, N)</c> and returns a non-trivial factor
    /// pair if either gcd lies strictly between 1 and N (preferring the smaller factor).
    /// </summary>
    public static (BigInteger G1, BigInteger G2, BigInteger? Factor1, BigInteger? Factor2) ExtractFactor(
        BigInteger x, BigInteger y, BigInteger n)
    {
        var g1 = BigInteger.GreatestCommonDivisor(BigInteger.Abs(x - y), n);
        var g2 = BigInteger.GreatestCommonDivisor(IntegerMath.Mod(x + y, n), n);

        var g1NonTrivial = g1 > 1 && g1 < n;
        var g2NonTrivial = g2 > 1 && g2 < n;
        if (!g1NonTrivial && !g2NonTrivial)
        {
            return (g1, g2, null, null);
        }

        var factor1 = g1NonTrivial && (!g2NonTrivial || g1 < g2) ? g1 : g2;
        return (g1, g2, factor1, n / factor1);
    }

    private static FactorResultRecord Invalid(string id, string reason)
        => new(id, FactorizationStatus.Invalid, null, null, null, null, reason);

    private static void Report(IProgress<SiqsProgressEvent>? progress, string message, int attempted, bool found)
    {
        progress?.Report(new SiqsProgressEvent(
            DateTimeOffset.UtcNow, null, SiqsPhase.SquareRoot, ProgressLevel.Info, message, null,
            new Dictionary<string, string>
            {
                [CounterKeys.DependenciesAttempted] = CounterFormat.Count(attempted),
                ["factor_found"] = CounterFormat.Bool(found),
            },
            null));
    }
}
