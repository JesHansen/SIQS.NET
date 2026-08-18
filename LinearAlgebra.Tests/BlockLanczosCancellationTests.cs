using LinearAlgebra;

namespace LinearAlgebra.Tests;

public sealed class BlockLanczosCancellationTests
{
    [Fact]
    public void Cancellation_after_a_lanczos_run_starts_interrupts_the_solve()
    {
        var rows = new[]
        {
            new RelationRow(0, 2),
            new RelationRow(1, 2),
            new RelationRow(0, 1),
        };
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelOnRunStart(cancellation);

        Assert.Throws<OperationCanceledException>(() => BlockLanczos.Solve(
            rows,
            columnCount: 3,
            progress: progress,
            cancellationToken: cancellation.Token));
        Assert.True(progress.Started);
    }

    private sealed class CancelOnRunStart(CancellationTokenSource cancellation)
        : IProgress<BlockLanczosProgress>
    {
        public bool Started { get; private set; }

        public void Report(BlockLanczosProgress value)
        {
            if (value.Stage == "run-start")
            {
                Started = true;
                cancellation.Cancel();
            }
        }
    }
}
