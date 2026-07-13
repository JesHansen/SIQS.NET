using System.Numerics;

namespace SIQS.Contracts;

/// <summary>The outcome of attempting to extract factors from one dependency (or a precheck).</summary>
public sealed record FactorResultRecord(
    string DependencyId,
    FactorizationStatus Status,
    BigInteger? GcdMinus,
    BigInteger? GcdPlus,
    BigInteger? Factor1,
    BigInteger? Factor2,
    string? Reason);
