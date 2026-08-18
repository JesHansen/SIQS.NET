using SIQS.Contracts;
using SIQS.Overlord;

namespace SIQS.Overlord.Tests;

public sealed class RelationInboxBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "siqs-inbox-boundary", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Quota_reservation_rolls_back_raw_bytes_when_total_retention_is_full()
    {
        var quota = new RelationInboxQuota(
            maxBacklogBytes: 16,
            maxInboxBytes: 16,
            initialRawBacklogBytes: 0,
            initialTotalBytes: 12);

        Assert.False(quota.TryReservePayload(8, out var reason));
        Assert.Equal(0, quota.RawBacklogBytes);
        Assert.Equal(12, quota.TotalBytes);
        Assert.Contains("total relation inbox", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_upload_reservations_cannot_exceed_raw_or_total_quota()
    {
        var inbox = CreateInbox(maxChunk: 8, maxBacklog: 8, maxTotal: 16);
        using var first = new MemoryStream("12345678"u8.ToArray());
        using var second = new MemoryStream("abcdefgh"u8.ToArray());

        var responses = await Task.WhenAll(
            inbox.StoreAsync("lease-one", 0, first, CancellationToken.None),
            inbox.StoreAsync("lease-two", 0, second, CancellationToken.None));

        Assert.Single(responses, response => response.Accepted);
        Assert.Single(responses, response => !response.Accepted);
        Assert.InRange(inbox.Snapshot().RawBacklogBytes, 0, 8);
        Assert.InRange(inbox.Snapshot().DurableBytes, 0, 16);
    }

    [Fact]
    public async Task Failed_upload_removes_temporary_file_and_empty_lease_directory()
    {
        var inbox = CreateInbox(maxChunk: 4, maxBacklog: 8, maxTotal: 16);
        using var body = new MemoryStream("oversized"u8.ToArray());

        var response = await inbox.StoreAsync("failed-lease", 0, body, CancellationToken.None);

        Assert.False(response.Accepted);
        Assert.False(Directory.Exists(Path.Combine(_root, ".relation-inbox", "failed-lease")));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(_root, ".relation-inbox"), "*.part", SearchOption.AllDirectories));
        inbox.Start();
        await inbox.SealAndDrainAsync();
    }

    [Fact]
    public async Task Completion_marker_is_idempotent_accounted_and_cleanup_preserves_canonical_artifacts()
    {
        Directory.CreateDirectory(_root);
        var canonical = Path.Combine(_root, "relations_000001.txt");
        await File.WriteAllTextAsync(canonical, "canonical");
        var inbox = CreateInbox(maxChunk: 8, maxBacklog: 8, maxTotal: 32);

        var first = await inbox.CompleteLeaseAsync("lease-one", 0, CancellationToken.None);
        var afterFirst = inbox.Snapshot().DurableBytes;
        var duplicate = await inbox.CompleteLeaseAsync("lease-one", 0, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(duplicate.Accepted);
        Assert.Equal(afterFirst, inbox.Snapshot().DurableBytes);
        Assert.True(afterFirst > 0);
        inbox.Start();
        await inbox.CleanupAsync(retain: false);
        Assert.False(Directory.Exists(Path.Combine(_root, ".relation-inbox")));
        Assert.Equal("canonical", await File.ReadAllTextAsync(canonical));
    }

    [Fact]
    public async Task Unknown_job_rejection_does_not_read_request_body()
    {
        await using var service = new OverlordService(_root);

        var response = await service.UploadChunkAsync(
            "D20260818-000000-0000", "lease-one", 0, new ThrowOnReadStream());

        Assert.False(response.Accepted);
        Assert.Contains("Unknown", response.Reason, StringComparison.Ordinal);
    }

    private DurableRelationInbox CreateInbox(long maxChunk, long maxBacklog, long maxTotal)
        => new(
            _root, maxChunk, maxBacklog, maxTotal,
            (IReadOnlyCollection<RawRelationRecord> _) => (0, 0),
            (_, _) => { },
            _ => { });

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ThrowOnReadStream : Stream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Rejected bodies must not be read.");

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
