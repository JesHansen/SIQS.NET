using System.Collections.ObjectModel;

namespace SIQS.Contracts;

/// <summary>
/// A single progress event reported by a phase or the pipeline. Counters are string values so
/// arbitrarily large integers and decimals serialize without precision loss. Phase libraries
/// report progress via <see cref="IProgress{T}"/> of this type (or a phase-specific event that
/// maps losslessly to it); the pipeline normalizes and writes these to <c>events.log</c>.
/// </summary>
public sealed record SiqsProgressEvent : IEquatable<SiqsProgressEvent>
{
    public SiqsProgressEvent(
        DateTimeOffset TimestampUtc,
        string? JobId,
        SiqsPhase Phase,
        ProgressLevel Level,
        string Message,
        double? Percent,
        IReadOnlyDictionary<string, string> Counters,
        string? ArtifactPath)
    {
        this.TimestampUtc = TimestampUtc;
        this.JobId = JobId;
        this.Phase = Phase;
        this.Level = Level;
        this.Message = Message;
        this.Percent = Percent;
        this.Counters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(Counters));
        this.ArtifactPath = ArtifactPath;
    }

    public DateTimeOffset TimestampUtc { get; }
    public string? JobId { get; init; }
    public SiqsPhase Phase { get; }
    public ProgressLevel Level { get; }
    public string Message { get; }
    public double? Percent { get; }
    public IReadOnlyDictionary<string, string> Counters { get; }
    public string? ArtifactPath { get; }

    /// <summary>Value equality including content-wise comparison of <see cref="Counters"/>.</summary>
    public bool Equals(SiqsProgressEvent? other)
    {
        if (other is null)
        {
            return false;
        }

        return TimestampUtc == other.TimestampUtc
            && JobId == other.JobId
            && Phase == other.Phase
            && Level == other.Level
            && Message == other.Message
            && Nullable.Equals(Percent, other.Percent)
            && ArtifactPath == other.ArtifactPath
            && StructuralEquality.DictionaryEqual(Counters, other.Counters);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TimestampUtc);
        hash.Add(JobId);
        hash.Add(Phase);
        hash.Add(Level);
        hash.Add(Message);
        hash.Add(Percent);
        hash.Add(ArtifactPath);
        hash.Add(Counters.Count);
        return hash.ToHashCode();
    }
}
