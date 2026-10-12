using System.Numerics;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

/// <summary>
/// Exercises the complete <see cref="SiqsPipeline.ResumeAsync"/> operation on sieve output: progress
/// from an interrupted sieve is reused, while output that failed validation is regenerated.
/// </summary>
public sealed class SieveResumeRepairTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "siqs-sieve-resume", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Resume_regenerates_raw_relations_that_failed_validation()
    {
        var pipeline = new SiqsPipeline();
        await pipeline.RunAsync(Request(), null, CancellationToken.None);
        var rawPath = Path.Combine(_directory, "relations_0000.txt");
        var document = RawRelationsFile.Parse(File.ReadAllText(rawPath));
        var corrupt = document.Relations[0] with { T = document.Relations[0].T + 1 };
        File.WriteAllText(rawPath, RawRelationsFile.Write(new RawRelationsDocument(
            document.Format, document.Metadata, [corrupt, .. document.Relations.Skip(1)])));
        MarkInterruptedBeforeSquareRoot();
        Assert.False(ArtifactValidator.Validate(SiqsPhase.Sieving, _directory, pipeline.NormalizeAndValidate(Request()), null).IsValid);

        var result = await pipeline.ResumeAsync(_directory, null, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedFactorFound, result.Status);
        var factorBase = FactorBaseFile.Parse(File.ReadAllText(Path.Combine(_directory, "factor_base.txt")));
        var relations = RawBatchFiles.Enumerate(_directory)
            .SelectMany(path => RawRelationsFile.Parse(File.ReadAllText(path)) is var raw
                ? raw.Relations.Select(relation => (raw.Metadata, relation))
                : [])
            .ToArray();
        Assert.DoesNotContain(relations, entry => entry.relation.Equals(corrupt));
        Assert.All(relations, entry => Assert.True(
            new RelationVerifier(factorBase, entry.Metadata.LargePrimeBound, entry.Metadata.LargePrime2Bound)
                .IsValid(entry.relation),
            entry.relation.RelationId));
        var events = File.ReadAllText(Path.Combine(_directory, "events.log"));
        Assert.Contains("resuming at sieving (artifacts failed validation); discarding sieving output", events);
        Assert.Contains("resume validation:", events);
    }

    [Fact]
    public async Task Resume_after_an_interrupted_sieve_retains_its_progress()
    {
        var pipeline = new SiqsPipeline();
        await pipeline.RunAsync(Request(), null, CancellationToken.None);
        var before = File.ReadAllText(Path.Combine(_directory, "relations_0000.txt"));
        var state = pipeline.LoadJob(_directory);
        state.Status = JobStatus.Canceled;
        foreach (var phase in state.PhaseStates.Where(phase => phase.Phase >= SiqsPhase.Sieving))
        {
            phase.Status = PhaseStatus.Pending;
        }

        JobStore.Write(_directory, state);

        var result = await pipeline.ResumeAsync(_directory, null, null, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedFactorFound, result.Status);
        Assert.StartsWith(
            string.Join('\n', before.Split('\n').Take(10)),
            File.ReadAllText(Path.Combine(_directory, "relations_0000.txt")));
        Assert.Contains("resuming at sieving (incomplete); retaining sieving output",
            File.ReadAllText(Path.Combine(_directory, "events.log")));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task Executor_discards_its_sieving_progress_only_when_sieve_output_is_discarded(
        bool interrupted, int expectedDiscards)
    {
        var fake = new FakePhaseExecutor();
        var pipeline = new SiqsPipeline(fake);
        var request = new FactorizationRequest(91) { RunDirectory = _directory };
        await pipeline.RunAsync(request, null, CancellationToken.None);
        var state = pipeline.LoadJob(_directory);
        state.Status = JobStatus.Canceled;
        foreach (var phase in state.PhaseStates.Where(phase => phase.Phase >= SiqsPhase.Sieving))
        {
            phase.Status = interrupted ? PhaseStatus.Pending : phase.Status;
        }

        JobStore.Write(_directory, state);
        if (!interrupted)
        {
            foreach (var path in RawBatchFiles.Enumerate(_directory)) File.WriteAllText(path, "garbage");
        }

        await pipeline.ResumeAsync(_directory, null, null, CancellationToken.None);

        Assert.Equal(expectedDiscards, fake.SievingProgressDiscards);
    }

    private void MarkInterruptedBeforeSquareRoot()
    {
        var state = JobStore.Load(_directory);
        state.Status = JobStatus.Canceled;
        state.PhaseStates.Single(phase => phase.Phase == SiqsPhase.SquareRoot).Status = PhaseStatus.Pending;
        JobStore.Write(_directory, state);
    }

    private FactorizationRequest Request()
        => new(BigInteger.Parse("1022117")) // 1009 * 1013
        {
            RunDirectory = _directory,
            FactorBase = new FactorBaseRunOptions { Bound = 1000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                ErrorMargin = 20,
                RelationTarget = 150,
                PolynomialCount = 200_000,
                Parallelism = 1,
            },
        };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
