using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Sieving;

namespace Sieving.Tests;

public class PolynomialSieveWorkerTests
{
    [Fact]
    public void Crossing_block_uses_the_lowest_candidate_threshold()
    {
        // Q(x) = x² - 4 has both roots inside this block. Its endpoints are positive,
        // so endpoint-only sampling would incorrectly set a positive gate.
        var threshold = PolynomialSieveWorker.MinThresholdByte(
            ad: 1, bd: 0, cd: -4, m: 3, blockStart: 0, blockEnd: 7,
            byteRescale: 10, logScale: 1, largePrimeLogAllowance: 0, errorMargin: 0);

        Assert.Equal(0, threshold);
    }

    [Fact]
    public void Crossing_detection_checks_the_vertex_when_endpoint_signs_match()
    {
        Assert.True(PolynomialSieveWorker.CrossesZeroInRange(
            ad: 1, bd: 0, cd: -4, m: 3, rangeStart: 0, rangeEnd: 7));

        Assert.False(PolynomialSieveWorker.CrossesZeroInRange(
            ad: 1, bd: 0, cd: 4, m: 3, rangeStart: 0, rangeEnd: 7));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(130, 129)]
    [InlineData(255, 254)]
    public void Vector_gate_preserves_values_equal_to_the_inclusive_threshold(
        byte minThreshold,
        byte expectedGate)
    {
        Assert.Equal(expectedGate, PolynomialSieveWorker.InclusiveVectorGate(minThreshold));
    }

    [Fact]
    public void Zero_threshold_cannot_use_the_vector_skip_gate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PolynomialSieveWorker.InclusiveVectorGate(0));
    }

    [Fact]
    public void Saturating_subtraction_retains_a_value_equal_to_the_threshold()
    {
        if (!Avx2.IsSupported)
        {
            return;
        }

        const byte minThreshold = 130;
        var values = Vector256.Create(minThreshold);
        var gate = Vector256.Create(PolynomialSieveWorker.InclusiveVectorGate(minThreshold));
        var saturated = Avx2.SubtractSaturate(values, gate);

        Assert.False(Avx2.TestZ(saturated.AsInt32(), saturated.AsInt32()));
    }
}
