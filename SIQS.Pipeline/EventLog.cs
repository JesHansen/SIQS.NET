using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>
/// Appends progress events to <c>events.log</c> as newline-delimited JSON and forwards them to an
/// optional caller-provided <see cref="IProgress{T}"/>. Disposing flushes and closes the file.
/// </summary>
public sealed class EventLog : IProgress<SiqsProgressEvent>, IDisposable
{
    public const string FileName = "events.log";

    private readonly StreamWriter _writer;
    private readonly IProgress<SiqsProgressEvent>? _forwardTo;
    private readonly string? _jobId;
    private readonly object _gate = new();

    public EventLog(string jobDirectory, string? jobId, IProgress<SiqsProgressEvent>? forwardTo)
    {
        _writer = new StreamWriter(Path.Combine(jobDirectory, FileName), append: true) { AutoFlush = true };
        _forwardTo = forwardTo;
        _jobId = jobId;
    }

    public void Report(SiqsProgressEvent value)
    {
        var stamped = value.JobId is null ? value with { JobId = _jobId } : value;
        lock (_gate)
        {
            _writer.WriteLine(SiqsProgressEventJson.Serialize(stamped));
        }

        _forwardTo?.Report(stamped);
    }

    public void Dispose() => _writer.Dispose();
}
