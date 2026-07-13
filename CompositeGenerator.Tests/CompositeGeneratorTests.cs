using System.Numerics;
using CompositeGenerator;

namespace CompositeGenerator.Tests;

public class CompositeRequestTests
{
    [Fact]
    public void Parses_single_digit_count()
    {
        var request = CompositeRequest.Parse(new[] { "52" });

        Assert.Equal(52, request.StartDigits);
        Assert.Equal(52, request.EndDigits);
        Assert.Equal(1, request.Count);
    }

    [Fact]
    public void Parses_range_and_count()
    {
        var request = CompositeRequest.Parse(new[] { "--range", "77", "78", "--count", "2" });

        Assert.Equal(77, request.StartDigits);
        Assert.Equal(78, request.EndDigits);
        Assert.Equal(2, request.Count);
    }

    [Fact]
    public void Rejects_mixed_single_and_range()
    {
        Assert.Throws<FormatException>(() => CompositeRequest.Parse(new[] { "52", "--range", "77", "78" }));
    }
}

public class SemiprimeGeneratorTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void Generates_requested_digit_count(int digits)
    {
        var n = SemiprimeGenerator.Generate(digits);

        Assert.Equal(digits, n.ToString().Length);
        Assert.True(n > BigInteger.One);
    }
}
