using SIQS.Contracts;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

public sealed class PhaseArtifactStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "siqs-artifact-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Canceled_write_never_publishes_an_artifact_or_leaves_a_temporary_file()
    {
        Directory.CreateDirectory(_directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new PhaseContext(
            "J20260818-000000-0000",
            _directory,
            new FactorizationRequest(91),
            Progress: null,
            CancellationToken: cancellation.Token);

        Assert.Throws<OperationCanceledException>(
            () => PhaseArtifactStore.Write(context, "dependencies.txt", "partial"));

        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
