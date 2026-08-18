using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using Sieving;

namespace SIQS.Pipeline;

/// <summary>
/// Validates the artifacts a phase produced (or, on resume, the artifacts already on disk): presence,
/// parseability, and metadata agreement with the job target and the factor base.
/// </summary>
internal static class ArtifactValidator
{
    public static ValidationResult Validate(
        SiqsPhase phase, string directory, FactorizationRequest request, PhaseResult? result)
    {
        var builder = new ValidationResultBuilder();
        var validateResumeInvariants = result is null;

        bool Exists(string name) => File.Exists(Path.Combine(directory, name));
        string Read(string name) => ArtifactFileIO.ReadAllText(Path.Combine(directory, name));

        T? Parse<T>(string name, Func<string, T> parse)
        {
            try
            {
                return parse(Read(name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
            {
                builder.Error("invalid_artifact", $"{name} could not be parsed: {ex.Message}");
                return default;
            }
        }

        FactorBaseDocument? factorBase = null;
        if (phase != SiqsPhase.FactorBase && Exists("factor_base.txt"))
        {
            factorBase = Parse("factor_base.txt", FactorBaseFile.Parse);
        }

        void CheckTarget(BigInteger targetN, BigInteger multiplier, BigInteger scaledN, string name)
        {
            builder.ErrorIf(targetN != request.TargetN,
                "metadata_mismatch", $"{name} target_n does not match the job target.");
            builder.ErrorIf(scaledN != targetN * multiplier,
                "metadata_mismatch", $"{name} scaled_n != target_n * multiplier.");
            if (factorBase is not null)
            {
                var fb = factorBase.Metadata;
                builder.ErrorIf(targetN != fb.TargetN || multiplier != fb.Multiplier || scaledN != fb.ScaledN,
                    "metadata_mismatch", $"{name} metadata disagrees with factor_base.txt.");
            }
        }

        switch (phase)
        {
            case SiqsPhase.FactorBase when result?.Factor is not null ||
                (result is not null && CounterFormat.ReadBool(result.Counters, CounterKeys.InputIsPrime)) ||
                (result is not null && CounterFormat.ReadBool(result.Counters, CounterKeys.InputIsProbablePrime)) ||
                (result is null && !Exists("factor_base.txt") && Exists("factors.txt")):
                builder.ErrorIf(!Exists("factors.txt"), "missing_artifact", "factors.txt was not produced.");
                if (Exists("factors.txt"))
                {
                    var factorDoc = Parse("factors.txt", FactorsFile.Parse);
                    if (factorDoc is not null)
                    {
                        CheckTarget(factorDoc.TargetN, factorDoc.Multiplier, factorDoc.ScaledN, "factors.txt");
                        if (validateResumeInvariants)
                        {
                            ArtifactInvariantValidator.Factors(factorDoc, builder);
                        }
                        builder.ErrorIf(factorDoc.Results.Count != 1,
                            "invalid_artifact", "factor-base factors.txt must contain exactly one precheck result.");
                        builder.ErrorIf(factorDoc.Results.Any(row => row.Status is not
                                (FactorizationStatus.InputPrime or FactorizationStatus.InputProbablePrime or
                                 FactorizationStatus.FactorFound)),
                            "invalid_artifact", "factor-base factors.txt contains a nonterminal precheck status.");
                        foreach (var row in factorDoc.Results.Where(row =>
                                     row.Status == FactorizationStatus.FactorFound))
                        {
                            builder.ErrorIf(row.Factor1 is not { } factor1 || row.Factor2 is not { } factor2 ||
                                factor1 <= 1 || factor2 <= 1 || factor1 >= request.TargetN ||
                                factor2 >= request.TargetN || factor1 * factor2 != request.TargetN,
                                "invalid_artifact", "factor-base factors.txt contains an invalid factor pair.");
                        }
                    }
                }

                break;
            case SiqsPhase.FactorBase:
                if (!Exists("factor_base.txt"))
                {
                    builder.Error("missing_artifact", "factor_base.txt was not produced.");
                }
                else
                {
                    var doc = Parse("factor_base.txt", FactorBaseFile.Parse);
                    if (doc is not null)
                    {
                        var fbMeta = doc.Metadata;
                        builder.ErrorIf(fbMeta.TargetN != request.TargetN,
                            "metadata_mismatch", "factor_base.txt target_n does not match the job target.");
                        builder.ErrorIf(fbMeta.ScaledN != fbMeta.TargetN * fbMeta.Multiplier,
                            "metadata_mismatch", "factor_base.txt scaled_n != target_n * multiplier.");
                        builder.ErrorIf(request.FactorBase.Bound is { } bound && fbMeta.Bound != bound,
                            "metadata_mismatch", "factor_base.txt bound does not match the stored request.");
                        if (validateResumeInvariants)
                        {
                            ArtifactInvariantValidator.FactorBase(doc, builder);
                        }
                    }
                }

                break;
            case SiqsPhase.Sieving:
                if (!Exists("factor_base.txt"))
                {
                    builder.Error("missing_artifact", "factor_base.txt is required to validate raw relations.");
                    break;
                }

                var rawPaths = RawBatchFiles.Enumerate(directory)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToArray();
                builder.ErrorIf(rawPaths.Length == 0, "missing_artifact", "no relations_*.txt or partials_*.txt produced.");
                var rawRelationIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var path in rawPaths)
                {
                    try
                    {
                        var doc = RawRelationsFile.Parse(ArtifactFileIO.ReadAllText(path));
                        CheckTarget(doc.Metadata.TargetN, doc.Metadata.Multiplier, doc.Metadata.ScaledN, Path.GetFileName(path));
                        if (factorBase is not null)
                        {
                            builder.ErrorIf(doc.Metadata.FactorBaseBound != factorBase.Metadata.Bound,
                                "metadata_mismatch", $"{Path.GetFileName(path)} factor_base_bound disagrees with factor_base.txt.");
                            if (validateResumeInvariants)
                            {
                                var expectedKind = Path.GetFileName(path).StartsWith("relations_", StringComparison.Ordinal)
                                    ? RelationKind.Full
                                    : RelationKind.Partial;
                                var verifier = new RelationVerifier(
                                    factorBase, doc.Metadata.LargePrimeBound, doc.Metadata.LargePrime2Bound);
                                ArtifactInvariantValidator.RawRelations(
                                    doc.Relations, Path.GetFileName(path), expectedKind, verifier, rawRelationIds, builder);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
                    {
                        builder.Error("invalid_artifact", $"{Path.GetFileName(path)} could not be parsed: {ex.Message}");
                    }
                }

                if (Exists(SieveCheckpointFile.FileName))
                {
                    try
                    {
                        var checkpoint = SieveCheckpointFile.Parse(Read(SieveCheckpointFile.FileName));
                        builder.ErrorIf(checkpoint.TargetN != request.TargetN,
                            "metadata_mismatch", "sieve_checkpoint.txt target_n does not match the job target.");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
                    {
                        builder.Error("invalid_artifact", $"sieve_checkpoint.txt could not be parsed: {ex.Message}");
                    }
                }

                break;
            case SiqsPhase.Filtering:
                builder.ErrorIf(!Exists("matrix_meta.txt"), "missing_artifact", "matrix_meta.txt missing.");
                builder.ErrorIf(!Exists("filtered_matrix.txt"), "missing_artifact", "filtered_matrix.txt missing.");
                builder.ErrorIf(!Exists("relations_filtered.txt"), "missing_artifact", "relations_filtered.txt missing.");
                if (!builder.IsValid)
                {
                    break;
                }

                var meta = Parse("matrix_meta.txt", MatrixMetaFile.Parse);
                var matrix = Parse("filtered_matrix.txt", FilteredMatrixFile.Parse);
                var relations = Parse("relations_filtered.txt", FilteredRelationsFile.Parse);
                if (meta is not null)
                {
                    CheckTarget(meta.TargetN, meta.Multiplier, meta.ScaledN, "matrix_meta.txt");
                    builder.ErrorIf(meta.MatrixFile != "filtered_matrix.txt",
                        "metadata_mismatch", "matrix_meta.txt matrix_file must be filtered_matrix.txt.");
                    builder.ErrorIf(meta.RelationsFile != "relations_filtered.txt",
                        "metadata_mismatch", "matrix_meta.txt relations_file must be relations_filtered.txt.");
                }

                if (relations is not null)
                {
                    CheckTarget(relations.TargetN, relations.Multiplier, relations.ScaledN, "relations_filtered.txt");
                    if (validateResumeInvariants && factorBase is not null)
                    {
                        ArtifactInvariantValidator.FilteredRelations(relations, factorBase, builder);
                    }
                }

                if (meta is not null && matrix is not null)
                {
                    builder.ErrorIf(matrix.Count != meta.RowCount,
                        "metadata_mismatch", "filtered_matrix.txt row count does not match matrix_meta.txt.");
                    if (validateResumeInvariants)
                    {
                        ArtifactInvariantValidator.Matrix(matrix, meta, relations, builder);
                    }
                }

                if (meta is not null && relations is not null)
                {
                    builder.ErrorIf(relations.Relations.Count != meta.RowCount,
                        "metadata_mismatch", "relations_filtered.txt row count does not match matrix_meta.txt.");
                }

                break;
            case SiqsPhase.LinearAlgebra:
                builder.ErrorIf(!Exists("dependencies.txt"), "missing_artifact", "dependencies.txt missing.");
                if (!Exists("matrix_meta.txt"))
                {
                    builder.Error("missing_artifact", "matrix_meta.txt is required to validate dependencies.txt.");
                }

                if (!builder.IsValid)
                {
                    break;
                }

                var deps = Parse("dependencies.txt", DependenciesFile.Parse);
                var matrixMeta = Parse("matrix_meta.txt", MatrixMetaFile.Parse);
                if (deps is not null)
                {
                    CheckTarget(deps.TargetN, deps.Multiplier, deps.ScaledN, "dependencies.txt");
                }

                if (deps is not null && matrixMeta is not null)
                {
                    builder.ErrorIf(deps.RowCount != matrixMeta.RowCount || deps.ColumnCount != matrixMeta.ColumnCount,
                        "metadata_mismatch", "dependencies.txt dimensions do not match matrix_meta.txt.");
                    if (validateResumeInvariants && Exists("filtered_matrix.txt"))
                    {
                        var dependencyMatrix = Parse("filtered_matrix.txt", FilteredMatrixFile.Parse);
                        if (dependencyMatrix is not null)
                        {
                            ArtifactInvariantValidator.Dependencies(deps, dependencyMatrix, builder);
                        }
                    }
                    else if (validateResumeInvariants)
                    {
                        builder.Error("missing_artifact", "filtered_matrix.txt is required to verify dependencies.txt.");
                    }
                }

                break;
            case SiqsPhase.SquareRoot:
                builder.ErrorIf(!Exists("factors.txt"), "missing_artifact", "factors.txt missing.");
                if (!builder.IsValid)
                {
                    break;
                }

                var factors = Parse("factors.txt", FactorsFile.Parse);
                if (factors is not null)
                {
                    CheckTarget(factors.TargetN, factors.Multiplier, factors.ScaledN, "factors.txt");
                    if (validateResumeInvariants)
                    {
                        ArtifactInvariantValidator.Factors(factors, builder);
                    }
                }

                break;
        }

        return builder.Build();
    }
}
