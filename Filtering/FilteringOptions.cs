namespace Filtering;

/// <summary>Optional filtering controls.</summary>
public sealed record FilteringOptions(
    int? MaxPartialsPerPrime = null,
    long? LargePrimeBound = null,
    long? LargePrime2Bound = null);
