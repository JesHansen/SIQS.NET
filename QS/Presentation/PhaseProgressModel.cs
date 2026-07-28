using System.Diagnostics;
using SIQS.Contracts;

namespace QS.Presentation;

/// <summary>Lifecycle state of a phase row in the live view.</summary>
internal enum PhaseRunState
{
    Pending,
    Active,
    Done,
}

/// <summary>An immutable snapshot of one phase row, safe to render off the update threads.</summary>
internal sealed record PhaseRowSnapshot(
    SiqsPhase Phase,
    PhaseRunState State,
    double? Fraction,
    string? Detail,
    string? Summary,
    TimeSpan Elapsed);

/// <summary>An immutable snapshot of the whole live view.</summary>
internal sealed record ProgressSnapshot(
    IReadOnlyList<PhaseRowSnapshot> Rows,
    IReadOnlyList<string> Notes);

/// <summary>
/// Thread-safe backing state for the live TUI. Progress events (raised on pipeline worker threads)
/// mutate it under a lock; the render loop reads immutable snapshots. Keeping the two apart lets all
/// Spectre updates happen on the render thread while the pipeline reports freely from its own.
/// </summary>
internal sealed class PhaseProgressModel
{
    private static readonly SiqsPhase[] Order =
    {
        SiqsPhase.FactorBase, SiqsPhase.Sieving, SiqsPhase.Filtering,
        SiqsPhase.LinearAlgebra, SiqsPhase.SquareRoot,
    };

    private readonly object _gate = new();
    private readonly Dictionary<SiqsPhase, Row> _rows = Order.ToDictionary(p => p, p => new Row(p));
    private readonly List<string> _notes = new();

    public void MarkActive(SiqsPhase phase)
    {
        lock (_gate)
        {
            if (_rows.TryGetValue(phase, out var row) && row.State == PhaseRunState.Pending)
            {
                row.State = PhaseRunState.Active;
                row.Watch.Restart();
            }
        }
    }

    public void ReportProgress(SiqsPhase phase, double fraction, string detail)
    {
        lock (_gate)
        {
            if (!_rows.TryGetValue(phase, out var row)) return;
            if (row.State == PhaseRunState.Pending) { row.State = PhaseRunState.Active; row.Watch.Restart(); }
            if (row.State == PhaseRunState.Done) return;
            row.Fraction = Math.Clamp(fraction, 0.0, 1.0);
            row.Detail = detail;
        }
    }

    /// <summary>Marks a phase done and activates the next pending phase in sequence.</summary>
    public void Complete(SiqsPhase phase, string summary)
    {
        lock (_gate)
        {
            if (_rows.TryGetValue(phase, out var row))
            {
                row.State = PhaseRunState.Done;
                row.Summary = summary;
                row.Fraction = 1.0;
                if (row.Watch.IsRunning) row.Watch.Stop();
            }

            var index = Array.IndexOf(Order, phase);
            if (index >= 0 && index + 1 < Order.Length)
            {
                var next = _rows[Order[index + 1]];
                if (next.State == PhaseRunState.Pending) { next.State = PhaseRunState.Active; next.Watch.Restart(); }
            }
        }
    }

    public void AddNote(string text)
    {
        lock (_gate) _notes.Add(text);
    }

    public ProgressSnapshot Snapshot()
    {
        lock (_gate)
        {
            var rows = Order
                .Select(p => _rows[p])
                .Select(r => new PhaseRowSnapshot(r.Phase, r.State, r.Fraction, r.Detail, r.Summary, r.Watch.Elapsed))
                .ToArray();
            return new ProgressSnapshot(rows, _notes.ToArray());
        }
    }

    private sealed class Row(SiqsPhase phase)
    {
        public SiqsPhase Phase { get; } = phase;
        public PhaseRunState State { get; set; } = PhaseRunState.Pending;
        public double? Fraction { get; set; }
        public string? Detail { get; set; }
        public string? Summary { get; set; }
        public Stopwatch Watch { get; } = new();
    }
}
