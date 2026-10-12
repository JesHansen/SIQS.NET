using QS_Filter;

namespace QS_Filter.Tests;

public sealed class FilteringCommandTests
{
    [Fact]
    public void Removed_partial_cap_option_is_rejected_instead_of_ignored()
    {
        var error = Assert.Throws<FormatException>(() =>
            FilteringCommand.Parse(["--relations", "relations_0000.txt", "--max-partials-per-prime", "1"]));

        Assert.Contains("--max-partials-per-prime was removed", error.Message);
    }

    [Fact]
    public void Supported_filtering_options_still_parse()
    {
        var command = FilteringCommand.Parse(
        [
            "--relations", "relations_0000.txt", "--out-dir", "out", "--max-cycle-length", "4",
            "--enable-two-merge", "false", "--two-merge-slack", "3", "--filter-spill-dir", "spill",
        ]);

        Assert.Equal(["relations_0000.txt"], command.RelationPaths);
        Assert.Equal("out", command.OutputDirectory);
        Assert.Equal(4, command.MaxCycleLength);
        Assert.False(command.EnableTwoMerge);
        Assert.Equal(3, command.TwoMergeSlack);
        Assert.Equal("spill", command.SpillDirectory);
    }
}
