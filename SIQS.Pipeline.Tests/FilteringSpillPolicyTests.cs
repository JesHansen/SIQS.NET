using System.Numerics;

namespace SIQS.Pipeline.Tests;

public class FilteringSpillPolicyTests
{
    [Fact]
    public void Small_composites_stay_in_memory()
    {
        // A C60 and a C99 crack without memory pressure, so spill (a ~10% time tax) stays off.
        Assert.False(FilteringSpillPolicy.ShouldSpill(BigInteger.Parse("1022117")));
        Assert.False(FilteringSpillPolicy.ShouldSpill(Composite(60)));
        Assert.False(FilteringSpillPolicy.ShouldSpill(Composite(99)));
    }

    [Fact]
    public void Composites_of_a_hundred_digits_or_more_spill()
    {
        Assert.True(FilteringSpillPolicy.ShouldSpill(Composite(100)));
        Assert.True(FilteringSpillPolicy.ShouldSpill(Composite(120)));
    }

    [Fact]
    public void The_threshold_is_a_hundred_digits()
    {
        Assert.Equal(100, FilteringSpillPolicy.SpillDigitThreshold);
        Assert.Equal(
            FilteringSpillPolicy.ShouldSpill(Composite(FilteringSpillPolicy.SpillDigitThreshold)),
            !FilteringSpillPolicy.ShouldSpill(Composite(FilteringSpillPolicy.SpillDigitThreshold - 1)));
    }

    // Smallest integer with exactly the given decimal-digit count (10^(digits-1)).
    private static BigInteger Composite(int digits) => BigInteger.Pow(10, digits - 1);
}
