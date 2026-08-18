using System.Numerics;
using Filtering;
using SIQS.Contracts;
using SIQS.Contracts.Files;

namespace Filtering.Tests;

public sealed class FilteringCancellationTests
{
    [Fact]
    public void Cancellation_after_a_raw_relation_interrupts_filtering()
    {
        using var cancellation = new CancellationTokenSource();
        var factorBase = new FactorBaseDocument(
            new FactorBaseMetadata(1000003, 1, 1000003, 50, 10.0),
            Enumerable.Range(1, 5)
                .Select(index => new FactorBaseEntry(index, index + 1, 0, 0, 1))
                .ToArray());
        var relations = CancelAfterFirst(cancellation);

        Assert.Throws<OperationCanceledException>(() => FilteringEngine.Run(
            factorBase,
            relations,
            Array.Empty<RawRelationRecord>(),
            new FilteringOptions(EnableTwoMerge: false),
            progress: null,
            cancellationToken: cancellation.Token));
    }

    private static IEnumerable<RawRelationRecord> CancelAfterFirst(CancellationTokenSource cancellation)
    {
        yield return Full("R00000000", 1);
        cancellation.Cancel();
        yield return Full("R00000001", 2);
    }

    private static RawRelationRecord Full(string id, int column)
        => new(
            id,
            RelationKind.Full,
            "P00000000",
            1,
            0,
            -1000,
            1,
            new BigInteger(3 + 2 * int.Parse(id[1..])),
            1,
            new Dictionary<int, int> { [column] = 1 },
            new[] { column },
            null);
}
