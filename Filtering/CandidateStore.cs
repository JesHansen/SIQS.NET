using System.Text;

namespace Filtering;

/// <summary>
/// Owns the payload half of every <see cref="Candidate"/> the engine produces. The in-memory store
/// keeps payloads resident (the historical behavior, byte-for-byte identical output); the spill store
/// streams them to a scratch file so peak memory holds only the light structural half.
/// </summary>
internal abstract class CandidateStore : IDisposable
{
    /// <summary>Records a candidate's payload and returns the candidate wired to retrieve it later.</summary>
    public abstract Candidate Add(CandidateParts parts);

    /// <summary>Retrieves a spilled payload; resident candidates never call back into the store.</summary>
    public abstract CandidatePayload Load(long token);

    public virtual void Dispose()
    {
    }
}

/// <summary>Keeps every payload resident, exactly as filtering did before candidate spill existed.</summary>
internal sealed class InMemoryCandidateStore : CandidateStore
{
    public override Candidate Add(CandidateParts parts) => Candidate.Resident(parts);

    public override CandidatePayload Load(long token)
        => throw new InvalidOperationException("Resident candidates do not load payloads from the store.");
}

/// <summary>
/// Streams candidate payloads to a scratch file in the spill directory, keeping only a byte offset per
/// candidate resident. The file is created with <see cref="FileOptions.DeleteOnClose"/>, so it never
/// outlives the run even if the process is killed after the handle opens. All payloads are appended
/// during candidate production; the survivors are read back (by seek) only once, while building output.
/// </summary>
internal sealed class SpillCandidateStore : CandidateStore
{
    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private readonly BinaryReader _reader;
    private long _appendOffset;

    public SpillCandidateStore(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"filter-candidates-{Guid.NewGuid():N}.bin");
        _stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 1 << 16, FileOptions.DeleteOnClose);
        _writer = new BinaryWriter(_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        _reader = new BinaryReader(_stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
    }

    public override Candidate Add(CandidateParts parts)
    {
        // Production is strictly append-only and precedes all reads; seek to the tracked end so a
        // stray read seek can never leave the write head mid-file.
        if (_stream.Position != _appendOffset)
        {
            _stream.Seek(_appendOffset, SeekOrigin.Begin);
        }

        var token = _appendOffset;
        CandidatePayloadCodec.Write(_writer, parts.Payload);
        _writer.Flush();
        _appendOffset = _stream.Position;
        return Candidate.Spilled(parts, this, token);
    }

    public override CandidatePayload Load(long token)
    {
        _stream.Seek(token, SeekOrigin.Begin);
        return CandidatePayloadCodec.Read(_reader);
    }

    public override void Dispose()
    {
        _reader.Dispose();
        _writer.Dispose();
        _stream.Dispose();
    }
}
