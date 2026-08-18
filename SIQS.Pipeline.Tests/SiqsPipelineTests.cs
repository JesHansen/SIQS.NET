using System.Numerics;
using SIQS.Contracts;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

public class SiqsPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "siqs-pipe-tests", Guid.NewGuid().ToString("N"));

    private string RunDir(string name) => Path.Combine(_root, name);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static FactorizationRequest Request(BigInteger n, string runDir) =>
        new(n) { RunDirectory = runDir };

    [Fact]
    public void NormalizeAndValidate_fills_factor_base_bound()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var normalized = pipeline.NormalizeAndValidate(new FactorizationRequest(BigInteger.Parse("1022117")));
        Assert.NotNull(normalized.FactorBase.Bound);
        Assert.True(normalized.FactorBase.Bound >= 1000);
    }

    [Fact]
    public void NormalizeAndValidate_rejects_n_le_1()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => pipeline.NormalizeAndValidate(new FactorizationRequest(1)));
    }

    [Fact]
    public void NormalizeAndValidate_accepts_zero_sieving_tuning_values()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());

        var normalized = pipeline.NormalizeAndValidate(new FactorizationRequest(BigInteger.Parse("1022117"))
        {
            Sieving = new SievingRunOptions { Parallelism = 0, BlockSize = 0 },
            LinearAlgebra = new LinearAlgebraRunOptions { Parallelism = 0 },
        });

        Assert.Equal(0, normalized.Sieving.Parallelism);
        Assert.Equal(0, normalized.Sieving.BlockSize);
        Assert.Equal(0, normalized.LinearAlgebra.Parallelism);
    }

    [Fact]
    public void NormalizeAndValidate_rejects_negative_sieving_tuning_values()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
            pipeline.NormalizeAndValidate(new FactorizationRequest(BigInteger.Parse("1022117"))
            {
                Sieving = new SievingRunOptions { Parallelism = -1 },
            }));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
            pipeline.NormalizeAndValidate(new FactorizationRequest(BigInteger.Parse("1022117"))
            {
                Sieving = new SievingRunOptions { BlockSize = -1 },
            }));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
            pipeline.NormalizeAndValidate(new FactorizationRequest(BigInteger.Parse("1022117"))
            {
                LinearAlgebra = new LinearAlgebraRunOptions { Parallelism = -1 },
            }));
    }

    [Fact]
    public void NormalizeAndValidate_rejects_unknown_cofactor_splitter()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
            pipeline.NormalizeAndValidate(new FactorizationRequest(BigInteger.Parse("1022117"))
            {
                Sieving = new SievingRunOptions { CofactorSplitter = "bad" },
            }));
    }

    [Fact]
    public async Task Runs_phases_in_order_and_completes_with_factor()
    {
        var fake = new FakePhaseExecutor();
        var pipeline = new SiqsPipeline(fake);

        var result = await pipeline.RunAsync(Request(91, RunDir("a")), null, CancellationToken.None);

        Assert.Equal(
            new[] { SiqsPhase.FactorBase, SiqsPhase.Sieving, SiqsPhase.Filtering, SiqsPhase.LinearAlgebra, SiqsPhase.SquareRoot },
            fake.Calls);
        Assert.Equal(JobStatus.CompletedFactorFound, result.Status);
        Assert.True(result.FactorFound);
        Assert.Equal(new BigInteger(91), result.Factors[0] * result.Factors[1]);
    }

    [Fact]
    public async Task Continues_after_filtering_with_small_positive_matrix_surplus()
    {
        var fake = new FakePhaseExecutor();
        fake.FilteringShapes.Enqueue((1073, 1000));
        var pipeline = new SiqsPipeline(fake);

        var request = Request(91, RunDir("small-surplus")) with { Sieving = new SievingRunOptions { RelationTarget = 1000 } };
        var result = await pipeline.RunAsync(request, null, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                SiqsPhase.FactorBase,
                SiqsPhase.Sieving,
                SiqsPhase.Filtering,
                SiqsPhase.LinearAlgebra,
                SiqsPhase.SquareRoot,
            },
            fake.Calls);
        Assert.Equal(JobStatus.CompletedFactorFound, result.Status);
        Assert.Single(fake.SievingRequests);
        Assert.Equal(1000, fake.SievingRequests[0].Sieving.RelationTarget);

        var job = pipeline.LoadJob(RunDir("small-surplus"));
        Assert.Equal("1000", job.Parameters["relation_target"]);
        Assert.All(job.PhaseStates, p => Assert.NotEqual(PhaseStatus.Failed, p.Status));
    }

    [Fact]
    public async Task Creates_workspace_with_job_json_and_event_log()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var dir = RunDir("b");
        await pipeline.RunAsync(Request(91, dir), null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(dir, "job.json")));
        Assert.True(File.Exists(Path.Combine(dir, "events.log")));
        var job = pipeline.LoadJob(dir);
        Assert.Equal(JobStatus.CompletedFactorFound, job.Status);
    }

    [Fact]
    public async Task Job_json_records_sieving_tuning_parameters()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var dir = RunDir("params");
        var request = new FactorizationRequest(91)
        {
            RunDirectory = dir,
            Sieving = new SievingRunOptions
            {
                PolynomialCount = 1234,
                ErrorMargin = 17,
                OutputBatchSize = 250,
                APrimeWindowSize = 42,
                Parallelism = 3,
                BlockSize = 4096,
            },
            LinearAlgebra = new LinearAlgebraRunOptions { Parallelism = 4 },
        };

        await pipeline.RunAsync(request, null, CancellationToken.None);

        var job = pipeline.LoadJob(dir);
        Assert.Equal("1234", job.Parameters["polynomial_count"]);
        Assert.Equal("17", job.Parameters["sieve_error_margin"]);
        Assert.Equal("250", job.Parameters["output_batch_size"]);
        Assert.Equal("42", job.Parameters["a_prime_window_size"]);
        Assert.Equal("3", job.Parameters["sieving_parallelism"]);
        Assert.Equal("4", job.Parameters["linear_algebra_parallelism"]);
        Assert.Equal("4096", job.Parameters["sieve_block_size"]);
    }

    [Fact]
    public async Task Job_json_parameter_key_set_is_stable()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var dir = RunDir("param-keys");

        await pipeline.RunAsync(Request(91, dir), null, CancellationToken.None);

        var job = pipeline.LoadJob(dir);
        var expected = new[]
        {
            "target_n", "factor_base_bound", "multiplier", "sieve_half_interval", "polynomial_count",
            "relation_target", "large_prime_bound", "large_prime2_bound", "large_prime2_threshold_bound",
            "cofactor_splitter", "two_large_primes", "sieve_error_margin", "output_batch_size",
            "a_prime_count", "a_prime_window_size", "sieving_parallelism", "sieve_block_size",
            "bucket_large_prime_cutoff", "resieve_large_prime_cutoff", "small_prime_variation_bound",
            "trial_sieve_percent",
            "linear_algebra_max_dependencies", "linear_algebra_parallelism",
            "continue_square_root_after_factor", "allow_tiny_input_trial_division",
        };
        Assert.Equal(expected.OrderBy(k => k), job.Parameters.Keys.OrderBy(k => k));
    }

    [Fact]
    public async Task Stored_parameters_round_trip_through_resume()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var dir = RunDir("round-trip");
        var request = new FactorizationRequest(91)
        {
            RunDirectory = dir,
            // FactorBaseBound/Multiplier must match the fake executor's fixed factor-base fixtures
            // (bound 1000, multiplier 1) so artifact validation passes; the round-trip still exercises
            // both keys through StoredParameterReader and ResumeOverrideValidator.
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 200000,
                PolynomialCount = 1234,
                RelationTarget = 900,
                LargePrimeBound = 1_000_000,
                ErrorMargin = 17,
                OutputBatchSize = 250,
                APrimeCount = 7,
                APrimeWindowSize = 42,
                Parallelism = 3,
                BlockSize = 4096,
                EnableTwoLargePrimes = true,
                LargePrime2Bound = 2_000_000,
                LargePrime2ThresholdBound = 1_500_000,
                CofactorSplitter = "squfof-rho",
            },
            LinearAlgebra = new LinearAlgebraRunOptions { MaxDependencies = 64, Parallelism = 4 },
        };

        await pipeline.RunAsync(request, null, CancellationToken.None);

        // Resuming while re-supplying the same explicit values reconstructs the request through
        // StoredParameterReader and compares it against the persisted map in ResumeOverrideValidator.
        // A faithful round-trip means matching overrides raise no conflict.
        var completed = pipeline.LoadJob(dir);
        Assert.Equal(JobStatus.CompletedFactorFound, completed.Status);
        var resumed = await pipeline.ResumeAsync(dir, request, null, CancellationToken.None);
        Assert.Equal(JobStatus.CompletedFactorFound, resumed.Status);
    }

    [Fact]
    public async Task Job_json_records_auto_for_omitted_sieving_tuning_parameters()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var dir = RunDir("auto-params");

        await pipeline.RunAsync(Request(91, dir), null, CancellationToken.None);

        var job = pipeline.LoadJob(dir);
        Assert.Equal("auto", job.Parameters["polynomial_count"]);
        Assert.Equal("auto", job.Parameters["sieve_error_margin"]);
        Assert.Equal("auto", job.Parameters["output_batch_size"]);
        Assert.Equal("auto", job.Parameters["a_prime_window_size"]);
        Assert.Equal("auto", job.Parameters["sieving_parallelism"]);
        Assert.Equal("auto", job.Parameters["linear_algebra_parallelism"]);
        Assert.Equal("auto", job.Parameters["sieve_block_size"]);
        Assert.Equal("auto", job.Parameters["small_prime_variation_bound"]);
    }

    [Fact]
    public async Task Forwards_linalg_parallelism_to_linear_algebra_phase()
    {
        var fake = new FakePhaseExecutor();
        var pipeline = new SiqsPipeline(fake);
        var request = Request(91, RunDir("linalg-parallelism")) with { LinearAlgebra = new LinearAlgebraRunOptions { Parallelism = 8 } };

        await pipeline.RunAsync(request, null, CancellationToken.None);

        var forwarded = Assert.Single(fake.LinearAlgebraRequests);
        Assert.Equal(8, forwarded.LinearAlgebra.Parallelism);
    }

    [Fact]
    public async Task Refuses_to_overwrite_non_empty_workspace()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var dir = RunDir("c");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "existing.txt"), "x");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.RunAsync(Request(91, dir), null, CancellationToken.None));
    }

    [Fact]
    public async Task Early_trivial_factor_skips_later_phases()
    {
        var fake = new FakePhaseExecutor { EarlyFactor = true };
        var pipeline = new SiqsPipeline(fake);

        var result = await pipeline.RunAsync(Request(100, RunDir("d")), null, CancellationToken.None);

        Assert.Equal(new[] { SiqsPhase.FactorBase }, fake.Calls);
        Assert.Equal(JobStatus.CompletedTrivialFactor, result.Status);
        Assert.True(result.FactorFound);
        var job = pipeline.LoadJob(RunDir("d"));
        Assert.All(job.PhaseStates.Skip(1), p => Assert.Equal(PhaseStatus.Skipped, p.Status));
    }

    [Fact]
    public async Task Phase_failure_stops_later_phases()
    {
        var fake = new FakePhaseExecutor { FailAt = SiqsPhase.Filtering };
        var pipeline = new SiqsPipeline(fake);

        var result = await pipeline.RunAsync(Request(91, RunDir("e")), null, CancellationToken.None);

        Assert.Equal(JobStatus.Failed, result.Status);
        Assert.DoesNotContain(SiqsPhase.LinearAlgebra, fake.Calls);
        Assert.NotNull(result.ErrorSummary);
    }

    [Fact]
    public async Task Resume_restarts_from_first_pending_or_missing_phase()
    {
        var fake = new FakePhaseExecutor();
        var pipeline = new SiqsPipeline(fake);
        var dir = RunDir("resume-linalg");

        await pipeline.RunAsync(Request(91, dir), null, CancellationToken.None);
        fake.Calls.Clear();

        File.Delete(Path.Combine(dir, "dependencies.txt"));
        File.Delete(Path.Combine(dir, "factors.txt"));
        var state = pipeline.LoadJob(dir);
        state.Status = JobStatus.Failed;
        state.CompletedUtc = null;
        state.ErrorSummary = new ErrorSummary { Phase = SiqsPhase.LinearAlgebra, Message = "interrupted" };
        foreach (var phaseState in state.PhaseStates.Where(p => p.Phase is SiqsPhase.LinearAlgebra or SiqsPhase.SquareRoot))
        {
            phaseState.Status = PhaseStatus.Pending;
            phaseState.Artifacts.Clear();
            phaseState.Counters.Clear();
        }

        JobStore.Write(dir, state);

        var result = await pipeline.ResumeAsync(dir, overrides: null, progress: null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedFactorFound, result.Status);
        Assert.Equal(new[] { SiqsPhase.LinearAlgebra, SiqsPhase.SquareRoot }, fake.Calls);
    }

    [Fact]
    public async Task Resume_rejects_conflicting_override()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var dir = RunDir("resume-conflict");
        await pipeline.RunAsync(Request(91, dir) with { Sieving = new SievingRunOptions { RelationTarget = 1000 } }, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ResumeAsync(dir, new FactorizationRequest(91) { Sieving = new SievingRunOptions { RelationTarget = 1001 } }, null, CancellationToken.None));

        Assert.Contains("relation_target", ex.Message);
    }

    [Fact]
    public async Task Resume_rejects_trial_sieve_jobs()
    {
        var pipeline = new SiqsPipeline(new FakePhaseExecutor());
        var dir = RunDir("resume-trial");
        await pipeline.RunAsync(Request(91, dir) with { TrialSievePercent = 10.0 }, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ResumeAsync(dir, null, null, CancellationToken.None));

        Assert.Contains("Trial-sieve", ex.Message);
    }

    [Fact]
    public async Task Underdetermined_linear_algebra_tops_up_and_retries()
    {
        var fake = new FakePhaseExecutor();
        fake.LinearAlgebraDeficits.Enqueue((998, 1000));
        var pipeline = new SiqsPipeline(fake);
        var dir = RunDir("top-up");

        var result = await pipeline.RunAsync(Request(91, dir) with { Sieving = new SievingRunOptions { RelationTarget = 1000 } }, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedFactorFound, result.Status);
        Assert.Equal(
            new[]
            {
                SiqsPhase.FactorBase,
                SiqsPhase.Sieving,
                SiqsPhase.Filtering,
                SiqsPhase.LinearAlgebra,
                SiqsPhase.Sieving,
                SiqsPhase.Filtering,
                SiqsPhase.LinearAlgebra,
                SiqsPhase.SquareRoot,
            },
            fake.Calls);
        Assert.Equal(2, fake.SievingRequests.Count);
        Assert.Equal(2002, fake.SievingRequests[1].Sieving.RelationTarget);

        var job = pipeline.LoadJob(dir);
        var round = Assert.Single(job.TopUpRounds);
        Assert.Equal(2, round.Deficit);
        Assert.Equal(2002, round.NewRelationTarget);
        Assert.Equal(2, job.PhaseStates.Single(p => p.Phase == SiqsPhase.LinearAlgebra).Attempts.Count);
    }

    [Fact]
    public async Task Cancellation_before_first_phase_yields_canceled()
    {
        var fake = new FakePhaseExecutor();
        var pipeline = new SiqsPipeline(fake);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await pipeline.RunAsync(Request(91, RunDir("f")), null, cts.Token);

        Assert.Equal(JobStatus.Canceled, result.Status);
        Assert.Empty(fake.Calls);
    }

    [Theory]
    [InlineData(SiqsPhase.FactorBase)]
    [InlineData(SiqsPhase.Sieving)]
    [InlineData(SiqsPhase.Filtering)]
    [InlineData(SiqsPhase.LinearAlgebra)]
    [InlineData(SiqsPhase.SquareRoot)]
    public async Task Cancellation_after_phase_work_begins_is_resumable(SiqsPhase phase)
    {
        var directory = RunDir($"cancel-{phase}");
        var fake = new FakePhaseExecutor { CancelAt = phase };
        var pipeline = new SiqsPipeline(fake);

        var canceled = await pipeline.RunAsync(Request(91, directory), null, CancellationToken.None);

        Assert.Equal(JobStatus.Canceled, canceled.Status);
        var canceledPhase = pipeline.LoadJob(directory).PhaseStates.Single(state => state.Phase == phase);
        Assert.Equal(PhaseStatus.Canceled, canceledPhase.Status);
        Assert.Null(canceledPhase.Error);

        fake.CancelAt = null;
        var resumed = await pipeline.ResumeAsync(directory, null, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedFactorFound, resumed.Status);
        Assert.True(fake.Calls.Count(call => call == phase) >= 2);
    }

    [Fact]
    public async Task Completed_no_factor_when_square_root_finds_nothing()
    {
        var fake = new FakePhaseExecutor { SquareRootFindsFactor = false };
        var pipeline = new SiqsPipeline(fake);

        var result = await pipeline.RunAsync(Request(91, RunDir("g")), null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedNoFactor, result.Status);
        Assert.False(result.FactorFound);
    }

    [Fact]
    public async Task Forwards_progress_events_to_caller()
    {
        // Use a synchronous capture since Progress<T> posts asynchronously.
        var captured = new List<SiqsProgressEvent>();
        var sync = new SynchronousProgress(captured);

        var pipeline = new SiqsPipeline(new ProgressEmittingExecutor());
        await pipeline.RunAsync(Request(91, RunDir("h")), sync, CancellationToken.None);

        Assert.Contains(captured, e => e.Phase == SiqsPhase.FactorBase);
        Assert.Contains(captured, e =>
            e.Phase == SiqsPhase.Pipeline
            && e.Message == "job workspace created"
            && e.Counters.TryGetValue("run_dir", out var runDir)
            && runDir == RunDir("h"));
        Assert.Contains(captured, e =>
            e.Message == "phase completed"
            && e.Counters.TryGetValue("elapsed_seconds", out var elapsed)
            && double.TryParse(elapsed, out _));
        var log = await File.ReadAllTextAsync(Path.Combine(RunDir("h"), "events.log"));
        Assert.Contains("factor_base", log);
    }

    private sealed class SynchronousProgress : IProgress<SiqsProgressEvent>
    {
        private readonly List<SiqsProgressEvent> _sink;
        public SynchronousProgress(List<SiqsProgressEvent> sink) => _sink = sink;
        public void Report(SiqsProgressEvent value) => _sink.Add(value);
    }

    private sealed class ProgressEmittingExecutor : IPhaseExecutor
    {
        private readonly FakePhaseExecutor _inner = new();

        public Task<PhaseResult> RunFactorBaseAsync(PhaseContext c)
        {
            c.Progress?.Report(new SiqsProgressEvent(DateTimeOffset.UtcNow, null, SiqsPhase.FactorBase,
                ProgressLevel.Info, "building factor base", null, new Dictionary<string, string>(), null));
            return _inner.RunFactorBaseAsync(c);
        }

        public Task<PhaseResult> RunSievingAsync(PhaseContext c) => _inner.RunSievingAsync(c);
        public Task<PhaseResult> RunFilteringAsync(PhaseContext c) => _inner.RunFilteringAsync(c);
        public Task<PhaseResult> RunLinearAlgebraAsync(PhaseContext c) => _inner.RunLinearAlgebraAsync(c);
        public Task<PhaseResult> RunSquareRootAsync(PhaseContext c) => _inner.RunSquareRootAsync(c);
    }
}
