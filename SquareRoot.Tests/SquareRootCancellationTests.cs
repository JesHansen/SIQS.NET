using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SquareRoot;

namespace SquareRoot.Tests;

public sealed class SquareRootCancellationTests
{
    [Fact]
    public void Cancellation_after_one_dependency_interrupts_square_root_processing()
    {
        var factorBase = new FactorBaseDocument(
            new FactorBaseMetadata(77, 1, 77, 100, 10.0),
            new[] { new FactorBaseEntry(1, 2, 0, 0, 1) });
        var relations = new FilteredRelationsDocument(77, 1, 77,
        [
            new FilteredRelationRecord(
                "F00000000", RelationKind.Full, new[] { "R00000000" }, 9, 1,
                new Dictionary<int, int> { [1] = 2 }, Array.Empty<int>(), null),
        ]);
        var dependencies = new DependenciesDocument(77, 1, 77, 1, 2,
        [
            new DependencyRecord(0, new[] { 0 }, new[] { "F00000000" }),
            new DependencyRecord(1, new[] { 0 }, new[] { "F00000000" }),
        ]);
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelAfterFactor(cancellation);

        Assert.Throws<OperationCanceledException>(() => SquareRootEngine.Run(
            factorBase,
            relations,
            dependencies,
            new SquareRootOptions(ContinueAfterFactor: true),
            progress,
            cancellation.Token));
        Assert.True(progress.FactorObserved);
    }

    private sealed class CancelAfterFactor(CancellationTokenSource cancellation)
        : IProgress<SiqsProgressEvent>
    {
        public bool FactorObserved { get; private set; }

        public void Report(SiqsProgressEvent value)
        {
            if (value.Message == "factor found")
            {
                FactorObserved = true;
                cancellation.Cancel();
            }
        }
    }
}
