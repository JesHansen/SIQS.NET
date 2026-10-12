using SIQS.Contracts.Distributed;

namespace SIQS.Overlord.Tests;

public sealed class DistributedJobDescriptorStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("siqs-descriptor-").FullName;

    [Fact]
    public void Relation_target_top_up_validates_against_the_persisted_descriptor()
    {
        DistributedJobDescriptorStore.ValidateOrCreate(_directory, Descriptor(Parameters()));

        var toppedUp = Parameters() with { RelationTarget = 5_000, TrialRawRelationTarget = 9_000 };

        DistributedJobDescriptorStore.ValidateOrCreate(_directory, Descriptor(toppedUp));
    }

    [Fact]
    public void Polynomial_domain_change_is_rejected()
    {
        DistributedJobDescriptorStore.ValidateOrCreate(_directory, Descriptor(Parameters()));

        var regeometried = Parameters() with { SieveHalfInterval = 40_000 };

        Assert.Throws<InvalidOperationException>(() =>
            DistributedJobDescriptorStore.ValidateOrCreate(_directory, Descriptor(regeometried)));
    }

    private static JobDescriptor Descriptor(SievingParameterSet parameters)
        => new("D20261012-000000-0000", "1022117", 1000, "1", false, parameters, 276,
            DistProtocol.ComputeParamHash(DistProtocol.Version, "1022117", 1000, "1", false, parameters));

    private static SievingParameterSet Parameters()
        => new(
            SieveHalfInterval: 20_000,
            PolynomialCount: 200_000,
            RelationTarget: 150,
            LargePrimeBound: 50_000,
            ErrorMargin: 20,
            OutputBatchSize: 1_000,
            APrimeCount: 2,
            APrimeWindowSize: 24,
            Parallelism: 1,
            SieveBlockSize: 32_768,
            BucketLargePrimeCutoff: 0,
            ResieveLargePrimeCutoff: 0,
            SmallPrimeVariationBound: 0,
            TrialRawRelationTarget: null,
            EnableTwoLargePrimes: false,
            LargePrime2Bound: 0,
            LargePrime2ThresholdBound: 0,
            CofactorSplitter: "auto");

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
