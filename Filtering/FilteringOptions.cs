namespace Filtering;

/// <summary>Optional filtering controls.</summary>
public sealed record FilteringOptions(
    long? LargePrimeBound = null,
    long? LargePrime2Bound = null,
    string? SpillDirectory = null,
    int? MaxCycleLength = null,
    bool EnableTwoMerge = true,
    int? TwoMergeSlack = null);
