using SIQS.Contracts;
using SIQS.Pipeline;

namespace SIQS.Pipeline.Tests;

public sealed class JobStateSnapshotTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "siqs-state-snapshot", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Snapshot_owns_nested_collections()
    {
        Directory.CreateDirectory(_directory);
        var state = new JobState
        {
            JobId = "J1",
            TargetN = "91",
            Status = JobStatus.Running,
            Parameters = new Dictionary<string, string> { ["relation_target"] = "100" },
            PhaseStates = new List<PhaseState>
            {
                new() { Phase = SiqsPhase.Sieving, Status = PhaseStatus.Running,
                    Counters = new Dictionary<string, string> { ["usable_relations"] = "4" } },
            },
        };

        var snapshot = JobStateSnapshots.From(state);
        state.Parameters["relation_target"] = "200";
        state.PhaseStates[0].Counters["usable_relations"] = "8";
        state.PhaseStates.Add(new PhaseState { Phase = SiqsPhase.Filtering });

        Assert.Equal("100", snapshot.Parameters["relation_target"]);
        Assert.Equal("4", snapshot.PhaseStates[0].Counters["usable_relations"]);
        Assert.Single(snapshot.PhaseStates);
    }

    [Fact]
    public void Job_json_round_trips_and_stays_schema_compatible()
    {
        Directory.CreateDirectory(_directory);
        var created = "2026-07-11T12:00:00.0000000+00:00";
        var state = new JobState
        {
            JobId = "J20260711-000001",
            TargetN = "1334471938438661103543925389295924469881",
            Status = JobStatus.CompletedFactorFound,
            CreatedUtc = created,
            StartedUtc = "2026-07-11T12:00:01.0000000+00:00",
            CompletedUtc = "2026-07-11T12:00:09.0000000+00:00",
            Parameters = new Dictionary<string, string> { ["relation_target"] = "900", ["cofactor_splitter"] = "squfof" },
            PhaseStates = new List<PhaseState>
            {
                new() { Phase = SiqsPhase.Sieving, Status = PhaseStatus.Completed, ElapsedSeconds = 6.5 },
            },
            FinalFactors = ["376143841477719544170112416779", "967199816677322233953999617963"],
            TopUpRounds = new List<TopUpRoundState> { new() { Round = 1, Deficit = 4, NewRelationTarget = 950 } },
        };

        JobStore.Write(_directory, state);
        var firstJson = File.ReadAllText(Path.Combine(_directory, "job.json"));

        // Round-trip: load, re-save, and assert the serialized form is byte-identical (schema stable).
        var reloaded = JobStore.Load(_directory);
        JobStore.Write(_directory, reloaded);
        var secondJson = File.ReadAllText(Path.Combine(_directory, "job.json"));
        Assert.Equal(firstJson, secondJson);

        // The persisted property names are lower_snake_case.
        Assert.Contains("\"job_id\":", firstJson);
        Assert.Contains("\"target_n\":", firstJson);
        Assert.Contains("\"created_utc\":", firstJson);
        Assert.Contains("\"final_factors\":", firstJson);

        // Typed snapshot view parses timestamps and numbers without ad-hoc parsing at call sites.
        var snapshot = JobStore.LoadSnapshot(_directory);
        Assert.Equal(DateTimeOffset.Parse(created), snapshot.CreatedAt);
        Assert.Equal(System.Numerics.BigInteger.Parse("1334471938438661103543925389295924469881"), snapshot.TargetNValue);
        Assert.Equal(2, snapshot.FinalFactorValues.Count);
        Assert.Equal(6.5, snapshot.PhaseStates[0].ElapsedSeconds);
    }

    [Fact]
    public void Snapshot_timestamp_accessor_rejects_invalid_persisted_value()
    {
        var snapshot = JobStateSnapshots.From(new JobState { CreatedUtc = "not-a-timestamp" });
        Assert.Throws<FormatException>(() => snapshot.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void JobTimestamp_parse_treats_missing_as_null(string? value)
        => Assert.Null(JobStateSnapshots.From(new JobState { StartedUtc = value }).StartedAt);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
