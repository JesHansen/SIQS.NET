namespace SIQS.Pipeline;

/// <summary>Records an incremental sieving top-up requested by an underdetermined matrix.</summary>
public sealed class TopUpRoundState
{
    public int Round { get; set; }
    public string? StartedUtc { get; set; }
    public int Deficit { get; set; }
    public long CurrentUsableRelations { get; set; }
    public int Margin { get; set; }
    public int NewRelationTarget { get; set; }
}
