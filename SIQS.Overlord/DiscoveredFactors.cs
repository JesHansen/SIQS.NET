using System.Numerics;

namespace SIQS.Overlord;

/// <summary>
/// The non-trivial factors a completed distributed job discovered, empty when none were found. Values
/// are decimal strings, matching the wire convention the distributed contracts use for big integers.
/// </summary>
public sealed record DiscoveredFactors(IReadOnlyList<string> Values)
{
    public static readonly DiscoveredFactors None = new(Array.Empty<string>());

    /// <summary>True when at least one non-trivial factor was found.</summary>
    public bool Any => Values.Count > 0;

    public static DiscoveredFactors From(IEnumerable<BigInteger> factors)
        => new(factors.Select(f => f.ToString()).ToArray());

    public override string ToString() => Any ? string.Join(" × ", Values) : "no non-trivial factor";
}
