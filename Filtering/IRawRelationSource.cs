using SIQS.Contracts;

namespace Filtering;

/// <summary>
/// A re-readable stream of raw relation records. <see cref="Enumerate"/> is called exactly once
/// per filtering run (Pass 1); <see cref="Materialize"/> re-reads only the records the engine
/// actually needs (Pass 2). Locators must be stable between the two calls.
/// </summary>
public interface IRawRelationSource
{
    /// <summary>Enumerates every record once, in deterministic order, with its locator.</summary>
    IEnumerable<(RawRelationLocator Locator, RawRelationRecord Record)> Enumerate();

    /// <summary>Re-reads the records at the given ascending, distinct locators.</summary>
    IEnumerable<(RawRelationLocator Locator, RawRelationRecord Record)> Materialize(
        IReadOnlyList<RawRelationLocator> ascendingLocators);
}
