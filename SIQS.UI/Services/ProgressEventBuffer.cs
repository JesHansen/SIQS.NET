using SIQS.Contracts;

namespace SIQS.UI.Services;

/// <summary>A fixed-capacity ring buffer of recent progress events for live log display.</summary>
public sealed class ProgressEventBuffer
{
    private readonly int _capacity;
    private readonly Queue<SiqsProgressEvent> _events = new();
    private readonly object _gate = new();

    public ProgressEventBuffer(int capacity = 500) => _capacity = capacity;

    public void Add(SiqsProgressEvent value)
    {
        lock (_gate)
        {
            _events.Enqueue(value);
            while (_events.Count > _capacity)
            {
                _events.Dequeue();
            }
        }
    }

    public IReadOnlyList<SiqsProgressEvent> Snapshot()
    {
        lock (_gate)
        {
            return _events.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }
}
