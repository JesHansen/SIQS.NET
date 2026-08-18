using SIQS.Contracts;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

public sealed class TerminalStateRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "siqs-terminal-recovery-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Recovers_factor_found_after_all_phases_complete()
    {
        var executor = new FakePhaseExecutor();
        var pipeline = new SiqsPipeline(executor);
        var directory = RunDirectory("factor-found");
        await pipeline.RunAsync(Request(91, directory), null, CancellationToken.None);
        MakeWholeJobTransitionStale(directory);
        executor.Calls.Clear();

        var recovered = await pipeline.ResumeAsync(directory, null, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedFactorFound, recovered.Status);
        Assert.Equal([7, 13], recovered.Factors);
        Assert.Equal(1, recovered.AttemptedDependencies);
        Assert.Empty(executor.Calls);
        Assert.NotNull(JobStore.Load(directory).CompletedUtc);
    }

    [Fact]
    public async Task Recovers_completed_no_factor_after_all_phases_complete()
    {
        var executor = new FakePhaseExecutor { SquareRootFindsFactor = false };
        var pipeline = new SiqsPipeline(executor);
        var directory = RunDirectory("no-factor");
        await pipeline.RunAsync(Request(91, directory), null, CancellationToken.None);
        MakeWholeJobTransitionStale(directory);
        executor.Calls.Clear();

        var recovered = await pipeline.ResumeAsync(directory, null, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedNoFactor, recovered.Status);
        Assert.False(recovered.FactorFound);
        Assert.Equal(1, recovered.AttemptedDependencies);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task Recovers_trivial_factor_precheck_and_skips_later_phases()
    {
        var executor = new FakePhaseExecutor { EarlyFactor = true };
        var pipeline = new SiqsPipeline(executor);
        var directory = RunDirectory("trivial");
        await pipeline.RunAsync(Request(100, directory), null, CancellationToken.None);
        MakePrecheckTransitionStale(directory);
        executor.Calls.Clear();

        var recovered = await pipeline.ResumeAsync(directory, null, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedTrivialFactor, recovered.Status);
        Assert.Equal([2, 50], recovered.Factors);
        Assert.Empty(executor.Calls);
        Assert.All(JobStore.Load(directory).PhaseStates.Skip(1),
            phase => Assert.Equal(PhaseStatus.Skipped, phase.Status));
    }

    [Fact]
    public async Task Recovers_prime_precheck_and_is_idempotent()
    {
        var executor = new FakePhaseExecutor { EarlyPrime = true };
        var pipeline = new SiqsPipeline(executor);
        var directory = RunDirectory("prime");
        await pipeline.RunAsync(Request(101, directory), null, CancellationToken.None);
        MakePrecheckTransitionStale(directory);
        executor.Calls.Clear();

        var recovered = await pipeline.ResumeAsync(directory, null, null, CancellationToken.None);
        var repeated = await pipeline.ResumeAsync(directory, null, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedPrime, recovered.Status);
        Assert.Equal(JobStatus.CompletedPrime, repeated.Status);
        Assert.Empty(executor.Calls);
        Assert.Empty(Directory.GetFiles(directory, $"{JobStore.FileName}.*.tmp"));
    }

    [Fact]
    public async Task Artifact_written_before_phase_completion_is_validated_then_phase_is_recomputed()
    {
        var executor = new FakePhaseExecutor();
        var pipeline = new SiqsPipeline(executor);
        var directory = RunDirectory("artifact-gap");
        await pipeline.RunAsync(Request(91, directory), null, CancellationToken.None);
        var state = JobStore.Load(directory);
        state.Status = JobStatus.Running;
        state.CompletedUtc = null;
        state.FinalFactors.Clear();
        var squareRoot = state.PhaseStates.Single(phase => phase.Phase == SiqsPhase.SquareRoot);
        squareRoot.Status = PhaseStatus.Running;
        JobStore.Write(directory, state);
        executor.Calls.Clear();

        var recovered = await pipeline.ResumeAsync(directory, null, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedFactorFound, recovered.Status);
        Assert.Equal([SiqsPhase.SquareRoot], executor.Calls);
    }

    [Fact]
    public async Task Invalid_precheck_factor_is_not_used_for_terminal_recovery()
    {
        var executor = new FakePhaseExecutor { EarlyFactor = true };
        var pipeline = new SiqsPipeline(executor);
        var directory = RunDirectory("invalid-precheck");
        await pipeline.RunAsync(Request(100, directory), null, CancellationToken.None);
        MakePrecheckTransitionStale(directory);
        var path = Path.Combine(directory, "factors.txt");
        File.WriteAllText(path, File.ReadAllText(path).Replace(",2,50,", ",3,50,", StringComparison.Ordinal));
        executor.Calls.Clear();

        var recovered = await pipeline.ResumeAsync(directory, null, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedTrivialFactor, recovered.Status);
        Assert.Equal([SiqsPhase.FactorBase], executor.Calls);
        Assert.Equal([2, 50], recovered.Factors);
    }

    private static FactorizationRequest Request(int target, string directory) =>
        new(target, RunDirectory: directory);

    private string RunDirectory(string name) => Path.Combine(_root, name);

    private static void MakeWholeJobTransitionStale(string directory)
    {
        var state = JobStore.Load(directory);
        state.Status = JobStatus.Running;
        state.CompletedUtc = null;
        state.FinalFactors.Clear();
        JobStore.Write(directory, state);
    }

    private static void MakePrecheckTransitionStale(string directory)
    {
        var state = JobStore.Load(directory);
        state.Status = JobStatus.Running;
        state.CompletedUtc = null;
        state.FinalFactors.Clear();
        foreach (var phase in state.PhaseStates.Skip(1))
        {
            phase.Status = PhaseStatus.Pending;
        }

        JobStore.Write(directory, state);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
