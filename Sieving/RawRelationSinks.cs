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
    private int _fullBatchCount;
    private int _partialBatchCount;

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
                if (_partialBatchCount + _partialBuffer.Count >= _batchSize)
                {
                    FlushPartials();
                }
            }
            else
            {
                _fullBuffer.Add(relation);
                if (_fullBatchCount + _fullBuffer.Count >= _batchSize)
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
            FlushFulls(durable: false);
            FlushPartials(durable: false);
        }
    }

    /// <summary>
    /// Flushes both buffers through the operating-system and device durability boundary. Distributed
    /// ingestion uses this before discarding its raw write-ahead payload.
    /// </summary>
    public void FlushDurable()
    {
        lock (_gate)
        {
            FlushFulls(durable: true);
            FlushPartials(durable: true);
        }
    }

    private void FlushFulls(bool durable = false)
        => FlushBuffer(
            "relations",
            _metadata.LargePrime2Bound is null ? FileFormats.RawRelationsV1 : FileFormats.RawRelationsV2,
            _fullBuffer,
            ref _fullBatch,
            ref _fullBatchCount,
            durable);

    private void FlushPartials(bool durable = false)
        => FlushBuffer(
            "partials",
            _metadata.LargePrime2Bound is null ? FileFormats.RawPartialsV1 : FileFormats.RawPartialsV2,
            _partialBuffer,
            ref _partialBatch,
            ref _partialBatchCount,
            durable);

    private void FlushBuffer(
        string prefix,
        string format,
        List<RawRelationRecord> buffer,
        ref int batch,
        ref int batchCount,
        bool durable)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        var name = RawBatchFileName.Create(prefix, batch).FileName;
        var path = Path.Combine(_outDir, name);
        var createsBatch = batchCount == 0;
        using var stream = new FileStream(
            path,
            createsBatch ? FileMode.CreateNew : FileMode.Append,
            FileAccess.Write,
            FileShare.Read);
        using var writer = new StreamWriter(stream);
        if (createsBatch)
        {
            RawRelationsFile.Write(
                writer,
                new RawRelationsDocument(format, _metadata, buffer));
            _artifacts.Add(name);
        }
        else
        {
            RawRelationsFile.WriteRecords(writer, format, buffer);
        }

        writer.Flush();
        if (durable)
        {
            stream.Flush(flushToDisk: true);
        }

        batchCount += buffer.Count;
        buffer.Clear();
        if (batchCount >= _batchSize)
        {
            batch++;
            batchCount = 0;
        }
    }

    private static int NextBatchIndex(string outDir, string prefix)
        => ExistingBatchIndexes(outDir, prefix).DefaultIfEmpty(-1).Max() + 1;

    private static IEnumerable<string> ExistingBatchNames(string outDir, string prefix)
        => ExistingBatchIndexes(outDir, prefix)
            .Order()
            .Select(index => RawBatchFileName.Create(prefix, index).FileName);

    private static IEnumerable<int> ExistingBatchIndexes(string outDir, string prefix)
    {
        if (!Directory.Exists(outDir))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(outDir, $"{prefix}_*.txt"))
        {
            if (RawBatchFileName.TryParse(Path.GetFileName(path), prefix, out var batch))
            {
                yield return batch.Index;
            }
        }
    }
}
