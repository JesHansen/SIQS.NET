using System.Numerics;

namespace SIQS.Pipeline;

/// <summary>
/// A request to factor <see cref="TargetN"/>. Run-wide controls sit at the top level; phase-owned
/// settings live in the grouped option records, which default to empty (all-defaults). Optional
/// values are filled from pipeline defaults by <see cref="SiqsPipeline.NormalizeAndValidate"/>.
/// </summary>
public sealed record FactorizationRequest(
    BigInteger TargetN,
    string? RunDirectory = null,
    double? TrialSievePercent = null)
{
    public FactorBaseRunOptions FactorBase { get; init; } = new();
    public SievingRunOptions Sieving { get; init; } = new();
    public LinearAlgebraRunOptions LinearAlgebra { get; init; } = new();
    public SquareRootRunOptions SquareRoot { get; init; } = new();
}
