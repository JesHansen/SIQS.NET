using Sieving;

namespace Sieving.Tests;

public class RelationKeyTests
{
    [Theory]
    [InlineData(12L, "R000007_0003_P000000000012")]
    [InlineData(-12L, "R000007_0003_M000000000012")]
    public void Formats_canonical_relation_and_polynomial_ids(long x, string expectedRelationId)
    {
        var key = new RelationKey(AIndex: 7, PolynomialIndex: 3, X: x);

        Assert.Equal(expectedRelationId, key.RelationId);
        Assert.Equal("P000007_0003", key.PolynomialId);
    }
}
