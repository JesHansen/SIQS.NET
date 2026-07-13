using System.Collections.Generic;

namespace SIQS.Contracts.Tests;

public class ProgressEventTests
{
    [Fact]
    public void Round_trips_through_json()
    {
        var evt = new SiqsProgressEvent(
            TimestampUtc: new DateTimeOffset(2026, 6, 17, 14, 30, 10, TimeSpan.Zero),
            JobId: "J20260617-143000-0001",
            Phase: SiqsPhase.Sieving,
            Level: ProgressLevel.Info,
            Message: "candidate accepted",
            Percent: 42.5,
            Counters: new Dictionary<string, string> { ["full_relations"] = "42" },
            ArtifactPath: "relations_0000.txt");

        var json = SiqsProgressEventJson.Serialize(evt);
        var back = SiqsProgressEventJson.Deserialize(json);

        Assert.Equal(evt, back);
    }

    [Fact]
    public void Json_uses_spec_tokens_for_phase_and_level()
    {
        var evt = new SiqsProgressEvent(
            new DateTimeOffset(2026, 6, 17, 14, 30, 10, TimeSpan.Zero),
            null, SiqsPhase.LinearAlgebra, ProgressLevel.Warning, "m", null,
            new Dictionary<string, string>(), null);

        var json = SiqsProgressEventJson.Serialize(evt);

        Assert.Contains("\"linear_algebra\"", json);
        Assert.Contains("\"warning\"", json);
    }

    [Fact]
    public void Json_is_single_line_for_event_log()
    {
        var evt = new SiqsProgressEvent(
            new DateTimeOffset(2026, 6, 17, 14, 30, 10, TimeSpan.Zero),
            "J1", SiqsPhase.Pipeline, ProgressLevel.Info, "start", null,
            new Dictionary<string, string>(), null);

        Assert.DoesNotContain("\n", SiqsProgressEventJson.Serialize(evt));
    }
}

public class ValidationResultTests
{
    [Fact]
    public void Ok_result_is_valid_with_no_issues()
    {
        var result = ValidationResult.Ok();
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Builder_aggregates_issues_and_marks_invalid()
    {
        var result = new ValidationResultBuilder()
            .Error("missing_key", "target_n missing")
            .Error("bad_value", "scaled_n mismatch")
            .Build();

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Issues.Count);
        Assert.Equal("missing_key", result.Issues[0].Code);
    }

    [Fact]
    public void Builder_with_no_errors_is_valid()
    {
        Assert.True(new ValidationResultBuilder().Build().IsValid);
    }

    [Fact]
    public void Merge_combines_multiple_results()
    {
        var a = new ValidationResultBuilder().Error("a", "1").Build();
        var b = new ValidationResultBuilder().Error("b", "2").Build();
        var merged = ValidationResult.Merge(a, b, ValidationResult.Ok());
        Assert.False(merged.IsValid);
        Assert.Equal(2, merged.Issues.Count);
    }
}

public class ArtifactDescriptorTests
{
    [Fact]
    public void Descriptor_holds_relative_path_and_metadata()
    {
        var d = new ArtifactDescriptor("factor_base.txt", ArtifactKind.FactorBase, SiqsPhase.FactorBase, "factor_base.txt", Required: true);
        Assert.Equal("factor_base.txt", d.Name);
        Assert.Equal(SiqsPhase.FactorBase, d.ProducedBy);
        Assert.True(d.Required);
    }
}
