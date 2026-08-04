using System.Numerics;
using Factorbase;
using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Distributed;
using SIQS.Contracts.Files;

namespace Sieving.Tests;

public class DistributedContractsTests
{
    private static FactorBaseDocument FactorBase()
        => FactorBaseGenerator.Generate(new FactorBaseOptions(BigInteger.Parse("1022117"), 1000, 1)).FactorBase!;

    [Fact]
    public void Sieving_parameters_round_trip_through_the_transport_set()
    {
        var parameters = SievingParameters.Default(FactorBase());

        Assert.Equal(parameters, parameters.ToSet().ToParameters());
    }

    [Fact]
    public void Param_hash_is_stable_sensitive_to_bounds_and_ignores_parallelism()
    {
        var set = SievingParameters.Default(FactorBase()).ToSet();
        string Hash(SievingParameterSet s) => DistProtocol.ComputeParamHash(DistProtocol.Version, "1022117", 1000, "1", false, s);

        var baseline = Hash(set);
        Assert.Equal(baseline, Hash(set));
        Assert.NotEqual(baseline, Hash(set with { LargePrimeBound = set.LargePrimeBound + 1 }));
        Assert.NotEqual(baseline, Hash(set with { APrimeWindowSize = set.APrimeWindowSize + 1 }));
        Assert.NotEqual(baseline, Hash(set with { SmallPrimeVariationBound = 256 }));
        Assert.Equal(baseline, Hash(set with { Parallelism = set.Parallelism + 4 }));
    }

    [Fact]
    public void Relation_round_trips_through_the_stream_record()
    {
        var relation = new RawRelationRecord(
            "r1", RelationKind.Partial, "p1", 17, -4, 1, 23, -99, -1,
            new Dictionary<int, int> { [2] = 3, [7] = 2 }, [2], null)
        {
            LargePrimes = [101, 103],
        };

        var roundTrip = RelationUploadCodec.FromUploadRecord(RelationUploadCodec.ToUploadRecord(relation));

        Assert.Equal(relation, roundTrip);
    }
}
