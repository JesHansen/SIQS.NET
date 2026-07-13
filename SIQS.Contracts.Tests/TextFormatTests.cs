using System.Numerics;
using SIQS.Contracts.Text;

namespace SIQS.Contracts.Tests;

public class MetadataFormatTests
{
    [Fact]
    public void Comment_writes_hash_prefixed_key_value()
    {
        Assert.Equal("# target_n=77", MetadataFormat.Comment("target_n", "77"));
    }

    [Fact]
    public void KeyValue_writes_bare_key_value()
    {
        Assert.Equal("row_count=1", MetadataFormat.KeyValue("row_count", "1"));
    }

    [Theory]
    [InlineData("# target_n=77", "target_n", "77")]
    [InlineData("target_n=77", "target_n", "77")]
    [InlineData("#format=siqs-factor-base-v1", "format", "siqs-factor-base-v1")]
    [InlineData("# columns=index,prime,root1,root2,logp", "columns", "index,prime,root1,root2,logp")]
    public void TryParse_handles_comment_and_bare_lines(string line, string key, string value)
    {
        Assert.True(MetadataFormat.TryParse(line, out var k, out var v));
        Assert.Equal(key, k);
        Assert.Equal(value, v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("# just a comment without equals")]
    [InlineData("1,2,0,0,255")]
    public void TryParse_rejects_non_key_value_lines(string line)
    {
        Assert.False(MetadataFormat.TryParse(line, out _, out _));
    }

    [Fact]
    public void ParseAll_collects_every_key_value_line()
    {
        var lines = new[]
        {
            "# format=siqs-matrix-meta-v1",
            "target_n=77",
            "multiplier=1",
            "row_id,relation_id,columns",
            "0,F00000000,",
        };
        var map = MetadataFormat.ParseAll(lines);
        Assert.Equal("siqs-matrix-meta-v1", map["format"]);
        Assert.Equal("77", map["target_n"]);
        Assert.Equal("1", map["multiplier"]);
        Assert.False(map.ContainsKey("row_id"));
    }
}

public class CsvTests
{
    [Fact]
    public void ParseLine_splits_simple_fields()
    {
        Assert.Equal(new[] { "R00000000", "full", "1", "" }, Csv.ParseLine("R00000000,full,1,"));
    }

    [Fact]
    public void ParseLine_handles_quoted_field_with_comma()
    {
        Assert.Equal(new[] { "a", "b,c", "d" }, Csv.ParseLine("a,\"b,c\",d"));
    }

    [Fact]
    public void ParseLine_handles_escaped_quote()
    {
        Assert.Equal(new[] { "say \"hi\"" }, Csv.ParseLine("\"say \"\"hi\"\"\""));
    }

    [Fact]
    public void WriteLine_quotes_fields_that_need_it()
    {
        Assert.Equal("a,\"b,c\",d", Csv.WriteLine(new[] { "a", "b,c", "d" }));
    }

    [Fact]
    public void WriteLine_quotes_embedded_quote()
    {
        Assert.Equal("\"say \"\"hi\"\"\"", Csv.WriteLine(new[] { "say \"hi\"" }));
    }

    [Fact]
    public void WriteLine_does_not_quote_plain_fields()
    {
        Assert.Equal("1,2,3", Csv.WriteLine(new[] { "1", "2", "3" }));
    }

    [Fact]
    public void Round_trip_preserves_fields()
    {
        var fields = new[] { "F0", "combined_partial", "R1 R2", "987654321", "-1", "0:1 2:4 7:2", "0", "101" };
        Assert.Equal(fields, Csv.ParseLine(Csv.WriteLine(fields)));
    }
}

public class IntegerListFormatTests
{
    [Fact]
    public void ParseInts_reads_space_separated_values()
    {
        Assert.Equal(new[] { 0, 4, 9 }, IntegerListFormat.ParseInts("0 4 9"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseInts_returns_empty_for_blank(string s)
    {
        Assert.Empty(IntegerListFormat.ParseInts(s));
    }

    [Fact]
    public void WriteInts_joins_with_spaces()
    {
        Assert.Equal("0 4 9", IntegerListFormat.WriteInts(new[] { 0, 4, 9 }));
    }

    [Fact]
    public void WriteInts_empty_is_empty_string()
    {
        Assert.Equal("", IntegerListFormat.WriteInts(Array.Empty<int>()));
    }

    [Fact]
    public void ParseBigIntegers_reads_large_values()
    {
        Assert.Equal(
            new[] { BigInteger.Parse("123456789012345678901234567890"), BigInteger.One },
            IntegerListFormat.ParseBigIntegers("123456789012345678901234567890 1"));
    }
}

public class ExponentMapFormatTests
{
    [Fact]
    public void Parse_reads_column_exponent_pairs()
    {
        var map = ExponentMapFormat.Parse("1:2 4:1 9:3");
        Assert.Equal(2, map[1]);
        Assert.Equal(1, map[4]);
        Assert.Equal(3, map[9]);
    }

    [Fact]
    public void Parse_empty_is_empty_map()
    {
        Assert.Empty(ExponentMapFormat.Parse(""));
    }

    [Fact]
    public void Write_sorts_by_column_ascending()
    {
        var map = new Dictionary<int, int> { [9] = 3, [1] = 2, [4] = 1 };
        Assert.Equal("1:2 4:1 9:3", ExponentMapFormat.Write(map));
    }

    [Fact]
    public void Write_omits_zero_exponents()
    {
        var map = new Dictionary<int, int> { [1] = 2, [4] = 0 };
        Assert.Equal("1:2", ExponentMapFormat.Write(map));
    }

    [Fact]
    public void ParityColumns_are_sorted_odd_exponent_columns()
    {
        var map = new Dictionary<int, int> { [0] = 1, [1] = 2, [4] = 3, [9] = 4 };
        Assert.Equal(new[] { 0, 4 }, ExponentMapFormat.ParityColumns(map));
    }
}
