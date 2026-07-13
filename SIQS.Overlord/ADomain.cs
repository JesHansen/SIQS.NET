using Sieving;
using SIQS.Contracts.Files;

namespace SIQS.Overlord;

/// <summary>
/// The deterministic A-polynomial index space for a job. Its size is the number of A-candidates the
/// sieve would enumerate, and it is the range distributed leases are carved from: both the server and
/// every client compute it identically from the factor base and resolved parameters.
/// </summary>
internal static class ADomain
{
    public static int Count(FactorBaseDocument factorBase, SievingParameters parameters)
        => PolynomialGenerator.SelectAPositions(FactorBaseData.From(factorBase), parameters).Count;
}
