using System.Numerics;

namespace SIQS.Contracts;

/// <summary>Metadata header shared by <c>factor_base.txt</c> and downstream files.</summary>
public sealed record FactorBaseMetadata(
    BigInteger TargetN,
    BigInteger Multiplier,
    BigInteger ScaledN,
    long Bound,
    double LogScale);

/// <summary>A single factor base prime row. Index 0 is reserved for the virtual sign column.</summary>
public sealed record FactorBaseEntry(
    int Index,
    long Prime,
    long Root1,
    long Root2,
    int LogP);
