using System.Numerics;
using SIQS.Contracts.Files;

namespace SIQS.Contracts.Tests;

public class FactorBaseFileTests
{
    private static FactorBaseDocument SampleDocument() => new(
        new FactorBaseMetadata(
            TargetN: 1000003,
            Multiplier: 3,
            ScaledN: 3000009,
            Bound: 50,
            LogScale: 255.0 / Math.Log(50)),
        new FactorBaseEntry[]
        {
            new(1, 2, 0, 0, 16),
            new(2, 3, 0, 0, 25),
            new(3, 11, 5, 6, 44),
        });

    [Fact]
    public void Write_emits_metadata_header_and_sorted_rows()
    {
        var text = FactorBaseFile.Write(SampleDocument());
        var lines = text.Split('\n');

        Assert.Equal("# format=siqs-factor-base-v1", lines[0]);
        Assert.Equal("# target_n=1000003", lines[1]);
        Assert.Equal("# multiplier=3", lines[2]);
        Assert.Equal("# scaled_n=3000009", lines[3]);
        Assert.Equal("# bound=50", lines[4]);
        Assert.StartsWith("# log_scale=", lines[5]);
        Assert.Equal("# columns=index,prime,root1,root2,logp", lines[6]);
        Assert.Equal("index,prime,root1,root2,logp", lines[7]);
        Assert.Equal("1,2,0,0,16", lines[8]);
        Assert.Equal("2,3,0,0,25", lines[9]);
        Assert.Equal("3,11,5,6,44", lines[10]);
    }

    [Fact]
    public void Round_trips_through_parse()
    {
        var doc = SampleDocument();
        var parsed = FactorBaseFile.Parse(FactorBaseFile.Write(doc));

        Assert.Equal(doc.Metadata.TargetN, parsed.Metadata.TargetN);
        Assert.Equal(doc.Metadata.Multiplier, parsed.Metadata.Multiplier);
        Assert.Equal(doc.Metadata.ScaledN, parsed.Metadata.ScaledN);
        Assert.Equal(doc.Metadata.Bound, parsed.Metadata.Bound);
        Assert.Equal(doc.Metadata.LogScale, parsed.Metadata.LogScale, 9);
        Assert.Equal(doc.Entries, parsed.Entries);
    }

    [Fact]
    public void Parses_end_to_end_example_fixture()
    {
        var text = string.Join('\n',
            "# format=siqs-factor-base-v1",
            "# target_n=77",
            "# multiplier=1",
            "# scaled_n=77",
            "# bound=2",
            "# log_scale=255",
            "# columns=index,prime,root1,root2,logp",
            "index,prime,root1,root2,logp",
            "1,2,0,0,255");

        var doc = FactorBaseFile.Parse(text);

        Assert.Equal(new BigInteger(77), doc.Metadata.TargetN);
        Assert.Equal(BigInteger.One, doc.Metadata.Multiplier);
        Assert.Equal(2, doc.Metadata.Bound);
        var entry = Assert.Single(doc.Entries);
        Assert.Equal(new FactorBaseEntry(1, 2, 0, 0, 255), entry);
    }

    [Fact]
    public void Parse_rejects_unknown_format()
    {
        var text = "# format=siqs-factor-base-v2\n# target_n=77\nindex,prime,root1,root2,logp\n";
        Assert.Throws<FormatException>(() => FactorBaseFile.Parse(text));
    }
}
