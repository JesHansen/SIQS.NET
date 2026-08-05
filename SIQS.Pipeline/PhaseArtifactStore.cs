using System.Numerics;
using System.Globalization;
using Factorbase;
using Filtering;
using LinearAlgebra;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using Sieving;
using SquareRoot;

namespace SIQS.Pipeline;

/// <summary>
/// Shared, path-safe artifact operations used by the focused phase runners.
/// </summary>
internal static class PhaseArtifactStore
{
    internal static string Read(PhaseContext context, string name)
        => ArtifactFileIO.ReadAllText(Path.Combine(context.JobDirectory, name));

    internal static void Write(PhaseContext context, string name, string content)
        => File.WriteAllText(Path.Combine(context.JobDirectory, name), content);

    internal static IRawRelationSource RawRelationSource(
        string jobDirectory,
        string searchPattern)
        => new RawRelationFileSource(
            EnumerateRawBatchFiles(jobDirectory, searchPattern.StartsWith("relations_", StringComparison.Ordinal) ? "relations" : "partials")
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray());

    internal static RawRelationsMetadata? ReadFirstRawMetadata(string jobDirectory, string searchPattern)
    {
        var path = EnumerateRawBatchFiles(jobDirectory, searchPattern.StartsWith("relations_", StringComparison.Ordinal) ? "relations" : "partials")
            .OrderBy(p => p, StringComparer.Ordinal)
            .FirstOrDefault();
        if (path is null)
        {
            return null;
        }

        using var reader = new StreamReader(path);
        return RawRelationsFile.ReadMetadata(reader);
    }

    internal static IEnumerable<RawRelationRecord> ReadRawRelations(
        IReadOnlyList<string> paths,
        FactorBaseMetadata factorBaseMetadata)
    {
        foreach (var path in paths)
        {
            using var metadataReader = new StreamReader(path);
            EnsureRawMetadataAgrees(RawRelationsFile.ReadMetadata(metadataReader), factorBaseMetadata, path);

            using var reader = new StreamReader(path);
            foreach (var (_, record) in RawRelationsFile.EnumerateWithOrdinals(reader))
            {
                yield return record;
            }
        }
    }

    internal static void EnsureRawMetadataAgrees(RawRelationsMetadata raw, FactorBaseMetadata factorBase, string path)
    {
        if (raw.TargetN != factorBase.TargetN || raw.Multiplier != factorBase.Multiplier || raw.ScaledN != factorBase.ScaledN)
        {
            throw new FormatException($"Relation file '{path}' metadata disagrees with factor_base.txt.");
        }

        if (raw.FactorBaseBound != factorBase.Bound)
        {
            throw new FormatException($"Relation file '{path}' factor_base_bound disagrees with factor_base.txt.");
        }
    }

    internal static IEnumerable<string> EnumerateRawBatchFiles(string jobDirectory)
        => EnumerateRawBatchFiles(jobDirectory, "relations")
            .Concat(EnumerateRawBatchFiles(jobDirectory, "partials"));

    internal static IEnumerable<string> EnumerateRawBatchFiles(string jobDirectory, string prefix)
        => Directory.EnumerateFiles(jobDirectory, $"{prefix}_*.txt")
            .Where(path => IsNumberedBatch(Path.GetFileName(path), prefix));

    internal static bool IsNumberedBatch(string name, string prefix)
        => RawBatchFileName.TryParse(name, prefix, out _);

    internal static void QuarantineCorruptTailBatch(
        string jobDirectory,
        string prefix,
        IProgress<SiqsProgressEvent>? progress)
    {
        var tail = EnumerateRawBatchFiles(jobDirectory, prefix)
            .Select(path =>
            {
                RawBatchFileName.TryParse(Path.GetFileName(path), prefix, out var batch);
                return (Path: path, batch.Index);
            })
            .OrderByDescending(x => x.Index)
            .FirstOrDefault();
        if (tail.Path is null)
        {
            return;
        }

        string? text = null;
        try
        {
            text = ArtifactFileIO.ReadAllText(tail.Path);
            RawRelationsFile.Parse(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            if (text is not null && TryRepairTruncatedTail(tail.Path, text, out var removedCharacters))
            {
                progress?.Report(new SiqsProgressEvent(
                    DateTimeOffset.UtcNow,
                    null,
                    SiqsPhase.Sieving,
                    ProgressLevel.Warning,
                    "repaired truncated tail batch",
                    Percent: null,
                    Counters: new Dictionary<string, string>
                    {
                        ["batch"] = Path.GetFileName(tail.Path),
                        ["removed_characters"] = removedCharacters.ToString(CultureInfo.InvariantCulture),
                    },
                    ArtifactPath: Path.GetFileName(tail.Path)));
                return;
            }

            var corrupt = UniqueCorruptPath(tail.Path);
            File.Move(tail.Path, corrupt);
            progress?.Report(new SiqsProgressEvent(
                DateTimeOffset.UtcNow,
                null,
                SiqsPhase.Sieving,
                ProgressLevel.Warning,
                "quarantined corrupt tail batch",
                Percent: null,
                Counters: new Dictionary<string, string>
                {
                    ["batch"] = Path.GetFileName(tail.Path),
                    ["reason"] = ex.Message,
                },
                ArtifactPath: Path.GetFileName(corrupt)));
        }
    }

    private static bool TryRepairTruncatedTail(string path, string text, out int removedCharacters)
    {
        removedCharacters = 0;
        if (text.Length == 0)
        {
            return false;
        }

        var lastContentIndex = text[^1] == '\n' ? text.Length - 2 : text.Length - 1;
        if (lastContentIndex < 0)
        {
            return false;
        }

        var previousLineBreak = text.LastIndexOf('\n', lastContentIndex);
        if (previousLineBreak < 0)
        {
            return false;
        }

        var repaired = text[..(previousLineBreak + 1)];
        try
        {
            RawRelationsFile.Parse(repaired);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }

        var temporaryPath = path + $".repair-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, repaired);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        removedCharacters = text.Length - repaired.Length;
        return true;
    }

    internal static string UniqueCorruptPath(string path)
    {
        var candidate = path + ".corrupt";
        var i = 1;
        while (File.Exists(candidate))
        {
            candidate = path + $".corrupt.{i}";
            i++;
        }

        return candidate;
    }

    internal static string FilteredRelationsFormat(FilteredRelationsDocument relations)
        => relations.Relations.Any(r => r.LargePrimes.Count > 1 || (r.LargePrimes.Count == 1 && r.LargePrime is null))
            ? FileFormats.FilteredRelationsV2
            : FileFormats.FilteredRelationsV1;

    internal sealed class LinearAlgebraProgressBridge : IProgress<BlockLanczosProgress>
    {
        private readonly PhaseContext _context;

        private LinearAlgebraProgressBridge(PhaseContext context) => _context = context;

        public static IProgress<BlockLanczosProgress>? For(PhaseContext context)
            => context.Progress is null ? null : new LinearAlgebraProgressBridge(context);

        public void Report(BlockLanczosProgress value)
        {
            var percent = value.Stage == "iteration" && value.TargetDimensions > 0
                ? Math.Clamp(100.0 * value.DimensionsSolved / value.TargetDimensions, 0.0, 100.0)
                : (double?)null;
            var level = value.Stage == "slow-convergence" ? ProgressLevel.Warning : ProgressLevel.Info;

            _context.Progress!.Report(new SiqsProgressEvent(
                DateTimeOffset.UtcNow,
                _context.JobId,
                SiqsPhase.LinearAlgebra,
                level,
                value.Stage,
                percent,
                new Dictionary<string, string>
                {
                    ["run"] = value.Run.ToString(CultureInfo.InvariantCulture),
                    ["iteration"] = value.Iteration.ToString(CultureInfo.InvariantCulture),
                    ["dimensions_solved"] = value.DimensionsSolved.ToString(CultureInfo.InvariantCulture),
                    ["target_dimensions"] = value.TargetDimensions.ToString(CultureInfo.InvariantCulture),
                    [CounterKeys.ElapsedSeconds] = CounterFormat.Seconds(value.Elapsed.TotalSeconds),
                },
                ArtifactPath: null));
        }
    }
}
