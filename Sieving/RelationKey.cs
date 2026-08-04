namespace Sieving;

/// <summary>
/// Canonical coordinates of a sieved relation. Formatting stays centralized here so a relation
/// cannot enter the worker's pending output without its stable relation and polynomial identities.
/// </summary>
internal readonly record struct RelationKey(int AIndex, int PolynomialIndex, long X)
{
    public string RelationId => $"R{AIndex:D6}_{PolynomialIndex:D4}_{XKey}";

    public string PolynomialId => $"P{AIndex:D6}_{PolynomialIndex:D4}";

    private string XKey => X < 0 ? $"M{Math.Abs(X):D12}" : $"P{X:D12}";
}
