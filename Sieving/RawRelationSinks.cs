using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Sieving;

public interface IRawRelationSink
{
    void Add(RawRelationRecord relation);
    void Flush();
    void Complete();
}

public sealed class InMemoryRawRelationSink : IRawRelationSink
{
    private readonly object _gate = new();
    private readonly List<RawRelationRecord> _fullRelations = [];
    private readonly List<RawRelationRecord> _partials = [];

    public IReadOnlyList<RawRelationRecord> FullRelations => _fullRelations;
    public IReadOnlyList<RawRelationRecord> Partials => _partials;

    public void Add(RawRelationRecord relation)
    {
        lock (_gate)
        {
            if (relation.Kind == RelationKind.Partial)
            {
                _partials.Add(relation);
            }
            else
            {
                _fullRelations.Add(relation);
            }
        }
    }

    public void Flush()
    {
    }

    public void Complete()
    {
        lock (_gate)
        {
            _fullRelations.Sort((a, b) => string.CompareOrdinal(a.RelationId, b.RelationId));
            _partials.Sort((a, b) => string.CompareOrdinal(a.RelationId, b.RelationId));
        }
    }
}

public sealed class RawRelationBatchFileSink : IRawRelationSink
{
    private readonly string _outDir;
    private readonly RawRelationsMetadata _metadata;
    private readonly int _batchSize;
    private readonly object _gate = new();
    private readonly List<RawRelationRecord> _fullBuffer;
    private readonly List<RawRelationRecord> _partialBuffer;
    private readonly List<string> _artifacts = [];
    private int _fullBatch;
    private int _partialBatch;

    public RawRelationBatchFileSink(string outDir, RawRelationsMetadata metadata, int batchSize)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be positive.");
        }

        _outDir = outDir;
        _metadata = metadata;
        _batchSize = batchSize;
        _fullBuffer = new List<RawRelationRecord>(batchSize);
        _partialBuffer = new List<RawRelationRecord>(batchSize);
        _fullBatch = NextBatchIndex(outDir, "relations");
        _partialBatch = NextBatchIndex(outDir, "partials");
        _artifacts.AddRange(ExistingBatchNames(outDir, "relations"));
        _artifacts.AddRange(ExistingBatchNames(outDir, "partials"));
    }

    public IReadOnlyList<string> Artifacts => _artifacts;

    public void Add(RawRelationRecord relation)
    {
        lock (_gate)
        {
            if (relation.Kind == RelationKind.Partial)
            {
                _partialBuffer.Add(relation);
                if (_partialBuffer.Count >= _batchSize)
                {
                    FlushPartials();
                }
            }
            else
            {
                _fullBuffer.Add(relation);
                if (_fullBuffer.Count >= _batchSize)
                {
                    FlushFulls();
                }
            }
        }
    }

    public void Complete()
    {
        Flush();
    }

    public void Flush()
    {
        lock (_gate)
        {
            FlushFulls();
            FlushPartials();
        }
    }

    private void FlushFulls()
    {
        if (_fullBuffer.Count == 0)
        {
            return;
        }

        WriteBatch("relations",
            _metadata.LargePrime2Bound is null ? FileFormats.RawRelationsV1 : FileFormats.RawRelationsV2,
            _fullBuffer, ref _fullBatch);
        _fullBuffer.Clear();
    }

    private void FlushPartials()
    {
        if (_partialBuffer.Count == 0)
        {
            return;
        }

        WriteBatch("partials",
            _metadata.LargePrime2Bound is null ? FileFormats.RawPartialsV1 : FileFormats.RawPartialsV2,
            _partialBuffer, ref _partialBatch);
        _partialBuffer.Clear();
    }

    private void WriteBatch(string prefix, string format, List<RawRelationRecord> buffer, ref int batch)
    {
        var name = $"{prefix}_{batch:D4}.txt";
        var path = Path.Combine(_outDir, name);
        var doc = new RawRelationsDocument(format, _metadata, buffer);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        RawRelationsFile.Write(writer, doc);
        _artifacts.Add(name);
        batch++;
    }

    private static int NextBatchIndex(string outDir, string prefix)
        => ExistingBatchIndexes(outDir, prefix).DefaultIfEmpty(-1).Max() + 1;

    private static IEnumerable<string> ExistingBatchNames(string outDir, string prefix)
        => ExistingBatchIndexes(outDir, prefix)
            .Order()
            .Select(index => $"{prefix}_{index:D4}.txt");

    private static IEnumerable<int> ExistingBatchIndexes(string outDir, string prefix)
    {
        if (!Directory.Exists(outDir))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(outDir, $"{prefix}_*.txt"))
        {
            var name = Path.GetFileName(path);
            if (name.Length != prefix.Length + 1 + 4 + ".txt".Length ||
                !name.StartsWith(prefix + "_", StringComparison.Ordinal) ||
                !name.EndsWith(".txt", StringComparison.Ordinal))
            {
                continue;
            }

            var digits = name.AsSpan(prefix.Length + 1, 4);
            if (int.TryParse(digits, out var index))
            {
                yield return index;
            }
        }
    }
}
