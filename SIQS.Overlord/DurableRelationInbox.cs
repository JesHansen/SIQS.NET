using System.Text;
using System.Text.Json;
using SIQS.Contracts;
using SIQS.Contracts.Distributed;

namespace SIQS.Overlord;

/// <summary>A point-in-time view of the durable relation inbox.</summary>
public sealed record RelationInboxSnapshot(
    long DurableBytes,
    long MaxDurableBytes,
    int ReadyChunks,
    long ReceivedChunks,
    long ProcessedChunks,
    long FailedChunks,
    long AcceptedRelations,
    long RejectedRelations);

/// <summary>
/// A disk-backed boundary between HTTP receipt and relation verification. Request threads only copy
/// bounded chunks to <c>.part</c> files, flush them, and atomically publish <c>.ready</c> files. One
/// background worker parses and ingests ready chunks. After canonical records cross their own
/// durability boundary, the large raw payload is replaced by a compact idempotency receipt.
/// </summary>
internal sealed class DurableRelationInbox
{
    private const string ChunkReadySuffix = ".ndjson.ready";
    private const string ChunkProcessingSuffix = ".ndjson.processing";
    // Legacy successful chunks used this suffix for the complete raw payload. Recovery replays them
    // once and converts them to the compact receipt format below.
    private const string ChunkDoneSuffix = ".ndjson.done";
    private const string ChunkFailedSuffix = ".ndjson.failed";
    private const string ChunkReceiptSuffix = ".receipt.done";
    private const string CompleteReadyName = "complete.ready";
    private const string CompleteDoneName = "complete.done";

    private readonly string _root;
    private readonly long _maxChunkBytes;
    private readonly long _maxSpoolBytes;
    private readonly Func<IReadOnlyCollection<RawRelationRecord>, (int Accepted, int Rejected)> _ingest;
    private readonly Action<string, bool> _leaseProcessed;
    private readonly Action<Exception> _faulted;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly TaskCompletionSource _drained =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task? _worker;
    private bool _accepting = true;
    private int _activeWriters;
    private long _durableBytes;
    private long _receivedChunks;
    private long _processedChunks;
    private long _failedChunks;
    private long _acceptedRelations;
    private long _rejectedRelations;

    public DurableRelationInbox(
        string jobDirectory,
        long maxChunkBytes,
        long maxSpoolBytes,
        Func<IReadOnlyCollection<RawRelationRecord>, (int Accepted, int Rejected)> ingest,
        Action<string, bool> leaseProcessed,
        Action<Exception> faulted)
    {
        if (maxChunkBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChunkBytes));
        }

        if (maxSpoolBytes < maxChunkBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSpoolBytes), "The spool quota must hold at least one maximum-size chunk.");
        }

        _root = Path.Combine(jobDirectory, ".relation-inbox");
        _maxChunkBytes = maxChunkBytes;
        _maxSpoolBytes = maxSpoolBytes;
        _ingest = ingest;
        _leaseProcessed = leaseProcessed;
        _faulted = faulted;
        Directory.CreateDirectory(_root);
        RecoverInterruptedChunks();
        _durableBytes = EnumerateChunkFiles(ChunkReadySuffix)
            .Concat(EnumerateChunkFiles(ChunkProcessingSuffix))
            .Concat(EnumerateChunkFiles(ChunkDoneSuffix))
            .Concat(EnumerateChunkFiles(ChunkFailedSuffix))
            .Concat(EnumerateChunkFiles(ChunkReceiptSuffix))
            .Sum(path => new FileInfo(path).Length);
    }

    public void Start()
    {
        lock (_gate)
        {
            _worker ??= Task.Run(ProcessLoopAsync);
        }

        Signal();
    }

    /// <summary>
    /// Whether the inbox can safely admit another worker. Existing leases may still have several
    /// concurrent uploads, so reserve at least one maximum-sized chunk before issuing more work.
    /// </summary>
    public bool CanAcceptLease
    {
        get
        {
            lock (_gate)
            {
                return _accepting && Interlocked.Read(ref _durableBytes) <= _maxSpoolBytes - _maxChunkBytes;
            }
        }
    }

    public async Task<RelationChunkResponse> StoreAsync(
        string leaseId,
        long sequence,
        Stream body,
        CancellationToken cancellationToken)
    {
        if (sequence < 0)
        {
            return new RelationChunkResponse(false, sequence, 0, false, "Chunk sequence cannot be negative.");
        }

        if (!TryBeginWrite())
        {
            return new RelationChunkResponse(false, sequence, 0, false, "The relation inbox is no longer accepting chunks.");
        }

        var leaseDirectory = LeaseDirectory(leaseId);
        Directory.CreateDirectory(leaseDirectory);
        var stem = sequence.ToString("D10");
        var readyPath = Path.Combine(leaseDirectory, stem + ChunkReadySuffix);
        var processingPath = Path.Combine(leaseDirectory, stem + ChunkProcessingSuffix);
        var donePath = Path.Combine(leaseDirectory, stem + ChunkDoneSuffix);
        var failedPath = Path.Combine(leaseDirectory, stem + ChunkFailedSuffix);
        var receiptPath = Path.Combine(leaseDirectory, stem + ChunkReceiptSuffix);
        var temporaryPath = Path.Combine(leaseDirectory, $"{stem}.{Guid.NewGuid():N}.part");
        long written = 0;

        try
        {
            if (TryFindExistingChunk(
                    readyPath, processingPath, donePath, failedPath, receiptPath, out var existingBytes))
            {
                return new RelationChunkResponse(true, sequence, existingBytes, true, null);
            }

            await using (var destination = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Mode = FileMode.CreateNew,
                                 Access = FileAccess.Write,
                                 Share = FileShare.None,
                                 BufferSize = 64 * 1024,
                                 Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                             }))
            {
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await body.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    if (written + read > _maxChunkBytes)
                    {
                        throw new InboxLimitException($"A relation chunk cannot exceed {_maxChunkBytes} bytes.");
                    }

                    written += read;
                    if (Interlocked.Add(ref _durableBytes, read) > _maxSpoolBytes)
                    {
                        throw new InboxLimitException($"The relation inbox quota of {_maxSpoolBytes} bytes is full.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, readyPath);
            }
            catch (IOException) when (TryFindExistingChunk(
                                          readyPath, processingPath, donePath, failedPath, receiptPath, out var racedBytes))
            {
                File.Delete(temporaryPath);
                Interlocked.Add(ref _durableBytes, -written);
                return new RelationChunkResponse(true, sequence, racedBytes, true, null);
            }

            Interlocked.Increment(ref _receivedChunks);
            Signal();
            return new RelationChunkResponse(true, sequence, written, false, null);
        }
        catch (InboxLimitException ex)
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            Interlocked.Add(ref _durableBytes, -written);
            return new RelationChunkResponse(false, sequence, 0, false, ex.Message);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            Interlocked.Add(ref _durableBytes, -written);
            throw;
        }
        finally
        {
            EndWrite();
        }
    }

    public async Task<LeaseUploadCompleteResponse> CompleteLeaseAsync(
        string leaseId,
        long chunkCount,
        CancellationToken cancellationToken)
    {
        if (chunkCount < 0)
        {
            return new LeaseUploadCompleteResponse(false, "Chunk count cannot be negative.");
        }

        if (!TryBeginWrite())
        {
            return new LeaseUploadCompleteResponse(false, "The relation inbox is no longer accepting lease markers.");
        }

        try
        {
            var leaseDirectory = LeaseDirectory(leaseId);
            Directory.CreateDirectory(leaseDirectory);
            var readyPath = Path.Combine(leaseDirectory, CompleteReadyName);
            var donePath = Path.Combine(leaseDirectory, CompleteDoneName);
            if (File.Exists(readyPath) || File.Exists(donePath))
            {
                return new LeaseUploadCompleteResponse(true, null);
            }

            await WriteDurableFileAsync(
                readyPath,
                Encoding.UTF8.GetBytes(chunkCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                cancellationToken);
            Signal();
            return new LeaseUploadCompleteResponse(true, null);
        }
        finally
        {
            EndWrite();
        }
    }

    public async Task SealAndDrainAsync()
    {
        lock (_gate)
        {
            _accepting = false;
        }

        Signal();
        await _drained.Task.ConfigureAwait(false);
    }

    public RelationInboxSnapshot Snapshot()
        => new(
            Interlocked.Read(ref _durableBytes),
            _maxSpoolBytes,
            EnumerateChunkFiles(ChunkReadySuffix).Count(),
            Interlocked.Read(ref _receivedChunks),
            Interlocked.Read(ref _processedChunks),
            Interlocked.Read(ref _failedChunks),
            Interlocked.Read(ref _acceptedRelations),
            Interlocked.Read(ref _rejectedRelations));

    private async Task ProcessLoopAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync().ConfigureAwait(false);

                while (await TryProcessOneChunkAsync().ConfigureAwait(false))
                {
                }

                while (await TryProcessOneCompletionAsync().ConfigureAwait(false))
                {
                }

                lock (_gate)
                {
                    if (!_accepting && _activeWriters == 0 && !HasReadyWork())
                    {
                        _drained.TrySetResult();
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _drained.TrySetException(ex);
            _faulted(ex);
        }
    }

    private async Task<bool> TryProcessOneChunkAsync()
    {
        var readyPath = EnumerateChunkFiles(ChunkReadySuffix)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        if (readyPath is null)
        {
            return false;
        }

        var processingPath = readyPath[..^ChunkReadySuffix.Length] + ChunkProcessingSuffix;
        try
        {
            File.Move(readyPath, processingPath);
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (IOException) when (!File.Exists(readyPath))
        {
            return true;
        }

        try
        {
            var relations = await ParseChunkAsync(processingPath).ConfigureAwait(false);
            var result = _ingest(relations);
            var receiptPath = processingPath[..^ChunkProcessingSuffix.Length] + ChunkReceiptSuffix;
            await CompactChunkAsync(
                processingPath,
                receiptPath,
                Encoding.UTF8.GetBytes(new FileInfo(processingPath).Length.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))).ConfigureAwait(false);

            Interlocked.Increment(ref _processedChunks);
            Interlocked.Add(ref _acceptedRelations, result.Accepted);
            Interlocked.Add(ref _rejectedRelations, result.Rejected);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or OverflowException)
        {
            var failedPath = processingPath[..^ChunkProcessingSuffix.Length] + ChunkFailedSuffix;
            await CompactChunkAsync(
                processingPath,
                failedPath,
                Encoding.UTF8.GetBytes(ex.Message)).ConfigureAwait(false);
            Interlocked.Increment(ref _failedChunks);
        }

        return true;
    }

    private async Task<bool> TryProcessOneCompletionAsync()
    {
        foreach (var readyPath in Directory.EnumerateFiles(_root, CompleteReadyName, SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var leaseDirectory = Path.GetDirectoryName(readyPath)!;
            var leaseId = Path.GetFileName(leaseDirectory);
            var text = await File.ReadAllTextAsync(readyPath).ConfigureAwait(false);
            if (!long.TryParse(text, out var chunkCount) || chunkCount < 0)
            {
                chunkCount = 0;
            }

            var allFinished = true;
            var success = true;
            for (long sequence = 0; sequence < chunkCount; sequence++)
            {
                var stem = Path.Combine(leaseDirectory, sequence.ToString("D10"));
                if (File.Exists(stem + ChunkFailedSuffix))
                {
                    success = false;
                }
                else if (!File.Exists(stem + ChunkReceiptSuffix) &&
                         !File.Exists(stem + ChunkDoneSuffix))
                {
                    allFinished = false;
                    break;
                }
            }

            if (!allFinished)
            {
                continue;
            }

            _leaseProcessed(leaseId, success);
            await WriteDurableFileAsync(
                Path.Combine(leaseDirectory, CompleteDoneName),
                Encoding.UTF8.GetBytes(success ? "accepted" : "failed"),
                CancellationToken.None).ConfigureAwait(false);
            File.Delete(readyPath);
            return true;
        }

        return false;
    }

    private static async Task<List<RawRelationRecord>> ParseChunkAsync(string path)
    {
        var relations = new List<RawRelationRecord>();
        using var reader = new StreamReader(
            path, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024);
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var wireRecord = JsonSerializer.Deserialize<RelationUploadRecord>(line, JsonSerializerOptions.Web)
                ?? throw new FormatException("A relation record cannot be null.");
            relations.Add(RelationUploadCodec.FromUploadRecord(wireRecord));
        }

        return relations;
    }

    private async Task WriteDurableFileAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.part";
        await using (var stream = new FileStream(
                         temporaryPath,
                         new FileStreamOptions
                         {
                             Mode = FileMode.CreateNew,
                             Access = FileAccess.Write,
                             Share = FileShare.None,
                             BufferSize = 4096,
                             Options = FileOptions.Asynchronous,
                         }))
        {
            await stream.WriteAsync(content, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        try
        {
            File.Move(temporaryPath, path);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(temporaryPath);
        }
    }

    private async Task CompactChunkAsync(string payloadPath, string receiptPath, byte[] receipt)
    {
        var payloadBytes = new FileInfo(payloadPath).Length;
        await WriteDurableFileAsync(receiptPath, receipt, CancellationToken.None).ConfigureAwait(false);
        var receiptBytes = new FileInfo(receiptPath).Length;
        File.Delete(payloadPath);
        Interlocked.Add(ref _durableBytes, receiptBytes - payloadBytes);
    }

    private void RecoverInterruptedChunks()
    {
        // Legacy versions retained every successful raw chunk. Replay each one and convert it to a
        // compact receipt; RelationIngest's fingerprints make this idempotent when the canonical
        // copy was already durable.
        foreach (var donePath in EnumerateChunkFiles(ChunkDoneSuffix))
        {
            var stem = donePath[..^ChunkDoneSuffix.Length];
            var readyPath = stem + ChunkReadySuffix;
            if (File.Exists(stem + ChunkReceiptSuffix))
            {
                File.Delete(donePath);
            }
            else if (!File.Exists(readyPath))
            {
                File.Move(donePath, readyPath);
            }
        }

        foreach (var processingPath in EnumerateChunkFiles(ChunkProcessingSuffix))
        {
            var stem = processingPath[..^ChunkProcessingSuffix.Length];
            if (File.Exists(stem + ChunkReceiptSuffix) ||
                File.Exists(stem + ChunkDoneSuffix) ||
                File.Exists(stem + ChunkFailedSuffix))
            {
                File.Delete(processingPath);
                continue;
            }

            File.Move(processingPath, stem + ChunkReadySuffix);
        }

        foreach (var partPath in Directory.EnumerateFiles(_root, "*.part", SearchOption.AllDirectories))
        {
            File.Delete(partPath);
        }
    }

    private IEnumerable<string> EnumerateChunkFiles(string suffix)
        => Directory.EnumerateFiles(_root, "*" + suffix, SearchOption.AllDirectories);

    private bool HasReadyWork()
        => EnumerateChunkFiles(ChunkReadySuffix).Any() ||
           Directory.EnumerateFiles(_root, CompleteReadyName, SearchOption.AllDirectories).Any();

    private string LeaseDirectory(string leaseId)
    {
        if (leaseId.Length is < 1 or > 64 ||
            leaseId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new ArgumentException("Invalid lease id.", nameof(leaseId));
        }

        return Path.Combine(_root, leaseId);
    }

    private bool TryBeginWrite()
    {
        lock (_gate)
        {
            if (!_accepting)
            {
                return false;
            }

            _activeWriters++;
            return true;
        }
    }

    private void EndWrite()
    {
        lock (_gate)
        {
            _activeWriters--;
        }

        Signal();
    }

    private void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private static bool TryFindExistingChunk(
        string readyPath,
        string processingPath,
        string donePath,
        string failedPath,
        string receiptPath,
        out long durableBytes)
    {
        foreach (var path in new[] { readyPath, processingPath })
        {
            if (File.Exists(path))
            {
                durableBytes = new FileInfo(path).Length;
                return true;
            }
        }

        if (File.Exists(donePath) || File.Exists(failedPath) || File.Exists(receiptPath))
        {
            durableBytes = 0;
            return true;
        }

        durableBytes = 0;
        return false;
    }

    private sealed class InboxLimitException(string message) : Exception(message);
}
