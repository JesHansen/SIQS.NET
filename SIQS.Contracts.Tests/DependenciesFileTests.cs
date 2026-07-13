using System.Numerics;
using SIQS.Contracts.Files;

namespace SIQS.Contracts.Tests;

public class DependenciesFileTests
{
    [Fact]
    public void Writes_metadata_and_dependency_rows()
    {
        var doc = new DependenciesDocument(
            TargetN: 77, Multiplier: 1, ScaledN: 77, RowCount: 10, ColumnCount: 5,
            Dependencies: new[]
            {
                new DependencyRecord(0, new[] { 0, 4, 9 }, new[] { "F00000000", "F00000004", "F00000009" }),
                new DependencyRecord(1, new[] { 2, 7 }, new[] { "F00000002", "F00000007" }),
            });

        var lines = DependenciesFile.Write(doc).Split('\n');
        Assert.Equal("# format=siqs-dependencies-v1", lines[0]);
        Assert.Equal("# dependency_count=2", lines[6]);
        Assert.Equal("dependency_id,row_ids,relation_ids", lines[8]);
        Assert.Equal("0,0 4 9,F00000000 F00000004 F00000009", lines[9]);
        Assert.Equal("1,2 7,F00000002 F00000007", lines[10]);
    }

    [Fact]
    public void Round_trips()
    {
        var doc = new DependenciesDocument(1000003, 3, 3000009, 120, 91, new[]
        {
            new DependencyRecord(0, new[] { 0 }, new[] { "F00000000" }),
            new DependencyRecord(1, new[] { 2, 3, 5 }, new[] { "F00000002", "F00000003", "F00000005" }),
        });

        var parsed = DependenciesFile.Parse(DependenciesFile.Write(doc));
        Assert.Equal(doc.TargetN, parsed.TargetN);
        Assert.Equal(doc.RowCount, parsed.RowCount);
        Assert.Equal(doc.ColumnCount, parsed.ColumnCount);
        Assert.Equal(doc.Dependencies, parsed.Dependencies);
    }
}
