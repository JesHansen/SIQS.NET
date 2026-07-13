using System.Numerics;
using SIQS.Contracts;

namespace Filtering;

/// <summary>
/// Combines single- and double-large-prime partials into full-equivalent candidates by finding
/// cycles in the large-prime graph. Pass 1 streams every partial, keeping only the union-find/forest
/// skeleton; pass 2 re-reads just the records that participate in a cycle.
/// </summary>
internal static class PartialCycleCombiner
{
    public static List<Candidate> Combine(
        IRawRelationSource partials, BigInteger scaledN, long bound, int factorBaseCount,
        FilteringOptions options, FilteringCounters counters)
    {
        // Pass 1: stream every partial once, retaining only the graph skeleton. Vertices are the
        // large primes as ulong ((q, 0) for 1LP, matching the sieving-side cycle tracker), forest
        // edges carry a record locator, and the duplicate set holds 128-bit fingerprints of the
        // canonical congruence keys instead of the key strings.
        var components = new Dictionary<ulong, ulong>();
        var forestParent = new Dictionary<ulong, (ulong Parent, RawRelationLocator Edge)>();
        var seenPartials = new HashSet<(ulong, ulong)>();
        var cycles = new List<RawRelationLocator[]>();

        foreach (var (locator, partial) in partials.Enumerate())
        {
            counters.RawPartials++;
            var largePrimes = RelationCongruence.NormalizeLargePrimes(partial);
            if (largePrimes.Count is < 1 or > 2)
            {
                throw new FormatException($"Partial relation '{partial.RelationId}' must have one or two large primes.");
            }

            // Drop mirrored re-finds of the same partial before cycle building: pairing a partial
            // with its own mirror forms a zero-parity cycle that can only produce a trivial dependency.
            if (!seenPartials.Add(Fingerprint.Of(RelationCongruence.CanonicalKey(partial.T, partial.FactorExponents, largePrimes, scaledN))))
            {
                counters.DuplicatesRemoved++;
                continue;
            }

            var max = largePrimes.Count == 1 ? options.LargePrimeBound : options.LargePrime2Bound ?? options.LargePrimeBound;
            foreach (var q in largePrimes)
            {
                if (q <= bound)
                {
                    throw new FormatException($"Large prime {q} is not greater than the factor base bound {bound}.");
                }

                if (max is { } maxValue && q > maxValue)
                {
                    throw new FormatException($"Large prime {q} exceeds the large-prime bound {maxValue}.");
                }
            }

            RelationValidation.ValidateColumns(partial.FactorExponents, factorBaseCount);
            RelationValidation.ValidateDeclaredParity(partial);

            var u = ToVertex(largePrimes[0]);
            var v = largePrimes.Count == 1 ? 0UL : ToVertex(largePrimes[1]);
            EnsureVertex(components, u);
            EnsureVertex(components, v);

            if (Find(components, u) == Find(components, v))
            {
                var path = FindForestPath(forestParent, u, v);
                path.Add(locator);
                counters.MaxCycleLength = Math.Max(counters.MaxCycleLength, path.Count);
                counters.TotalCycleLength += path.Count;
                counters.CombinedPartials++;
                cycles.Add(path.ToArray());
            }
            else
            {
                LinkComponents(components, forestParent, u, v, locator);
            }
        }

        components.Clear();
        forestParent.Clear();
        seenPartials.Clear();

        // Pass 2: re-read only the cycle members and combine each cycle into a candidate.
        var byLocator = MaterializeCycleMembers(partials, cycles);
        var candidates = new List<Candidate>(cycles.Count);
        foreach (var cycle in cycles)
        {
            var members = new PartialLite[cycle.Length];
            for (var i = 0; i < cycle.Length; i++)
            {
                members[i] = byLocator[cycle[i]];
            }

            candidates.Add(Candidate.FromCombined(members, scaledN));
        }

        return candidates;
    }

    private static Dictionary<RawRelationLocator, PartialLite> MaterializeCycleMembers(
        IRawRelationSource partials, List<RawRelationLocator[]> cycles)
    {
        var needed = new HashSet<RawRelationLocator>();
        foreach (var cycle in cycles)
        {
            foreach (var locator in cycle)
            {
                needed.Add(locator);
            }
        }

        var ascending = needed.ToArray();
        Array.Sort(ascending);

        var byLocator = new Dictionary<RawRelationLocator, PartialLite>(ascending.Length);
        foreach (var (locator, record) in partials.Materialize(ascending))
        {
            byLocator[locator] = PartialLite.From(record);
        }

        if (byLocator.Count != ascending.Length)
        {
            throw new InvalidOperationException("A cycle member could not be re-read from the partial input.");
        }

        return byLocator;
    }

    private static ulong ToVertex(BigInteger largePrime)
        => largePrime.Sign > 0 && largePrime <= ulong.MaxValue
            ? (ulong)largePrime
            : throw new FormatException($"Large prime {largePrime} does not fit the 64-bit large-prime graph vertex encoding.");

    private static void EnsureVertex(Dictionary<ulong, ulong> components, ulong vertex)
    {
        if (!components.ContainsKey(vertex))
        {
            components[vertex] = vertex;
        }
    }

    private static ulong Find(Dictionary<ulong, ulong> components, ulong x)
    {
        // Iterative two-pass find with full path compression: recursion here can overflow the
        // stack on the multi-million-vertex chains large runs produce.
        var root = x;
        while (components[root] != root)
        {
            root = components[root];
        }

        while (components[x] != root)
        {
            var next = components[x];
            components[x] = root;
            x = next;
        }

        return root;
    }

    private static void LinkComponents(
        Dictionary<ulong, ulong> components,
        Dictionary<ulong, (ulong Parent, RawRelationLocator Edge)> forestParent,
        ulong child,
        ulong parent,
        RawRelationLocator edge)
    {
        var childRoot = Find(components, child);
        var parentRoot = Find(components, parent);
        if (childRoot == parentRoot)
        {
            return;
        }

        Reroot(forestParent, child);
        forestParent[child] = (parent, edge);
        components[childRoot] = parentRoot;
    }

    private static void Reroot(
        Dictionary<ulong, (ulong Parent, RawRelationLocator Edge)> forestParent,
        ulong newRoot)
    {
        ulong? previous = null;
        RawRelationLocator previousEdge = default;
        var current = newRoot;

        while (forestParent.TryGetValue(current, out var next))
        {
            var oldParent = next.Parent;
            var oldEdge = next.Edge;

            if (previous is null)
            {
                forestParent.Remove(current);
            }
            else
            {
                forestParent[current] = (previous.Value, previousEdge);
            }

            previous = current;
            previousEdge = oldEdge;
            current = oldParent;
        }

        if (previous is not null)
        {
            forestParent[current] = (previous.Value, previousEdge);
        }
    }

    private static List<RawRelationLocator> FindForestPath(
        Dictionary<ulong, (ulong Parent, RawRelationLocator Edge)> forestParent,
        ulong start,
        ulong end)
    {
        var startAncestors = new Dictionary<ulong, List<RawRelationLocator>>();
        var current = start;
        var path = new List<RawRelationLocator>();
        startAncestors[current] = [];
        while (forestParent.TryGetValue(current, out var next))
        {
            path = new List<RawRelationLocator>(path) { next.Edge };
            current = next.Parent;
            startAncestors[current] = path;
        }

        current = end;
        var endPath = new List<RawRelationLocator>();
        List<RawRelationLocator>? startPath = null;
        while (!startAncestors.TryGetValue(current, out startPath))
        {
            if (!forestParent.TryGetValue(current, out var next))
            {
                throw new InvalidOperationException("Large-prime forest path was expected but not found.");
            }

            endPath.Add(next.Edge);
            current = next.Parent;
        }

        var result = new List<RawRelationLocator>(startPath.Count + endPath.Count);
        result.AddRange(startPath);
        result.AddRange(endPath);
        return result;
    }
}
