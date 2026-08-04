using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Factorbase;
using Sieving;

namespace SIQS.Benchmarks;

internal static class CandidateResieveComparisonTool
{
    public static int Run(string[] args)
    {
        if (args.Length is < 1 or > 8
            || !BigInteger.TryParse(args[0], out var target)
            || target <= 1)
        {
            Console.Error.WriteLine(
                "usage: --compare-candidate-resieve <target> [raw-target] " +
                "[parallelism] [repetitions] [focused] [polynomials] " +
                "[wide2lp] [block-size]");
            return 1;
        }

        var rawTarget = ParseOptional(args, 1, 5_000);
        var parallelism = ParseOptional(args, 2, 16);
        var repetitions = ParseOptional(args, 3, 3);
        var factorBase = FactorBaseGenerator.Generate(new FactorBaseOptions(target)).FactorBase
            ?? throw new InvalidOperationException(
                "Target factored during factor-base precheck.");
        var defaults = SievingParameters.Default(factorBase);
        var baseline = defaults with
        {
            Parallelism = parallelism,
            TrialRawRelationTarget = rawTarget,
            PolynomialCount = ParseOptional(
                args, 5, (int)Math.Min(int.MaxValue, defaults.PolynomialCount)),
            SieveBlockSize = ParseOptional(args, 7, defaults.SieveBlockSize),
            EnableDetailedSieveTiming = true,
        };
        if (args.Length >= 7
            && args[6].Equals("wide2lp", StringComparison.OrdinalIgnoreCase))
        {
            var largePrimeBound = checked(80L * factorBase.Metadata.Bound);
            baseline = baseline with
            {
                LargePrimeBound = largePrimeBound,
                EnableTwoLargePrimes = true,
                LargePrime2Bound = largePrimeBound,
                LargePrime2ThresholdBound =
                    (long)Math.Floor(Math.Pow(largePrimeBound, 0.9)),
            };
        }

        var focus = args.Length >= 5 ? args[4] : "promotion";
        var bucketFocus = focus is "cutoffs" or "combined-cutoffs" or "resieve-cutoff"
            or "cutoff-model"
            or "block-size"
            or "next-wave"
            or "production-only"
            or "bucket-capacity"
            or "bucket-capacity-leading"
            or "bucket-capacity-safety"
            or "third-wave"
            or "candidate-only" or "sweep" or "leading" or "policy" or "current";
        int[] thresholds = focus is "cutoffs" or "combined-cutoffs" or "resieve-cutoff"
            or "cutoff-model"
            or "block-size"
            or "next-wave"
            or "production-only"
            or "bucket-capacity"
            or "bucket-capacity-leading"
            or "bucket-capacity-safety"
            or "third-wave"
            or "root-state" or "loads"
            or "vector2" or "vector16" or "combined"
            or "combined-only" or "adaptive" or "candidate-only" or "sweep"
            or "leading" or "policy" or "current" or "promotion"
            ? [2]
            : focus.Equals("focused", StringComparison.OrdinalIgnoreCase)
                ? [1, 2]
                : [1, 2, 3, int.MaxValue];
        var modes = SelectModes(focus);

        Console.WriteLine(
            "mode,min_candidates,rep,elapsed_ms,known_hit_cpu_ms,trial_cpu_ms," +
            "scan_cpu_ms,direct_blocks,resieved_blocks,polynomials,candidates," +
            "raw_relations,usable_relations,bucket_binary_blocks,bucket_candidate_blocks," +
            "bucket_map_blocks,bucket_hit_inspections,bucket_vector_groups," +
            "bucket_matching_masks,bucket_map_probes,bucket_decoded_hits," +
            "init_ms,clear_ms,small_fill_ms,direct_fill_ms,bucket_scatter_ms," +
            "bucket_replay_ms,direct_known_ms,bucket_known_ms," +
            "bucket_slab_bytes,bucket_overflow,bucket_max_hits,bucket_capacity," +
            "trial_pre_ms,trial_post_ms,trial_a_ms,trial_check_ms,trial_parity_ms");
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            var orderedThresholds = (repetition & 1) == 0
                ? thresholds.Reverse()
                : thresholds;
            foreach (var threshold in orderedThresholds)
            {
                var orderedModes = (repetition & 1) == 0 ? modes.Reverse() : modes;
                foreach (var mode in orderedModes)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    var watch = Stopwatch.StartNew();
                    var result = SievingEngine.Sieve(
                        factorBase,
                        Configure(baseline, mode, bucketFocus) with
                        {
                            CandidateResieveMinimumCandidates = threshold,
                        });
                    watch.Stop();
                    WriteMeasurement(mode, threshold, repetition, watch, result.Counters);
                }
            }
        }

        return 0;
    }

    private static string[] SelectModes(string focus) => focus switch
    {
        "loads" => ["constructed-loads", "contiguous-loads"],
        "root-state" => ["full-position-state", "root-only-bucket-state"],
        "next-wave" => ["pre-next-wave", "next-wave"],
        "production-only" => ["production"],
        "bucket-capacity" =>
        ["capacity-1050", "capacity-1100", "capacity-1250", "capacity-1500", "capacity-2000"],
        "bucket-capacity-leading" =>
        ["capacity-1050", "capacity-1100", "capacity-2000"],
        "bucket-capacity-safety" => ["capacity-1050"],
        "third-wave" => ["pre-third-wave", "third-wave"],
        "cutoff-model" => ["bucket-655360", "bucket-786432", "bucket-1048576"],
        "cutoffs" =>
        [
            "bucket-524288", "bucket-655360", "bucket-786432",
            "bucket-917504", "bucket-1048576",
        ],
        "resieve-cutoff" =>
        ["resieve-131072", "resieve-262144", "resieve-393216", "resieve-524288"],
        "combined-cutoffs" =>
        [
            "combined-1048576-262144", "combined-655360-262144",
            "combined-655360-393216", "combined-655360-524288",
            "combined-786432-393216", "combined-786432-524288",
        ],
        "block-size" => ["block-262144", "block-524288", "block-1048576"],
        "promotion" => ["legacy", "simd-medium", "adaptive-bucket", "promoted"],
        "combined-only" => ["prime", "combined16"],
        "combined" => ["prime", "vector16", "combined16"],
        "vector16" => ["prime", "vector16"],
        "adaptive" => ["prime", "vector4", "vector8", "vector16", "vector"],
        "vector2" => ["prime", "vector"],
        "candidate-only" => ["binary", "candidate"],
        "sweep" =>
        [
            "binary", "candidate", "map-3", "hybrid-4", "hybrid-8", "hybrid-16",
        ],
        "leading" => ["binary", "geometry-adaptive"],
        "policy" or "current" =>
        [
            "binary", "candidate", "map-3", "adaptive-1", "adaptive-2",
            "geometry-adaptive",
        ],
        _ => ["prime", "candidate", "vector"],
    };

    private static SievingParameters Configure(
        SievingParameters baseline, string mode, bool bucketFocus)
    {
        if (mode == "constructed-loads")
            return baseline with { UseContiguousVectorResieveLoads = false };
        if (mode == "contiguous-loads")
            return baseline with { UseContiguousVectorResieveLoads = true };
        if (mode == "full-position-state")
            return baseline with { UseRootOnlyBucketState = false };
        if (mode == "root-only-bucket-state")
            return baseline with { UseRootOnlyBucketState = true };
        if (mode == "pre-next-wave")
            return baseline with
            {
                UseRootOnlyBucketState = false,
                BucketLargePrimeCutoff = 1_048_576,
                SmallPrimeVariationBound = 256,
            };
        if (mode == "next-wave") return baseline;
        if (mode == "production") return baseline;
        if (mode == "third-wave") return baseline;
        if (mode == "pre-third-wave")
            return baseline with
            {
                BucketCapacityPermille = 2_000,
                BucketLargePrimeCutoff = baseline.SieveHalfInterval > 8_388_608
                    ? 655_360
                    : baseline.BucketLargePrimeCutoff,
            };
        if (mode.StartsWith("capacity-", StringComparison.Ordinal))
            return baseline with
            {
                BucketCapacityPermille = int.Parse(
                    mode.AsSpan("capacity-".Length), CultureInfo.InvariantCulture),
            };
        if (mode.StartsWith("bucket-", StringComparison.Ordinal))
            return baseline with
            {
                UseRootOnlyBucketState = true,
                BucketLargePrimeCutoff = int.Parse(
                    mode.AsSpan("bucket-".Length), CultureInfo.InvariantCulture),
            };
        if (mode.StartsWith("resieve-", StringComparison.Ordinal))
            return baseline with
            {
                UseRootOnlyBucketState = true,
                ResieveLargePrimeCutoff = int.Parse(
                    mode.AsSpan("resieve-".Length), CultureInfo.InvariantCulture),
            };
        if (mode.StartsWith("combined-", StringComparison.Ordinal))
        {
            var separators = mode.Split('-');
            return baseline with
            {
                UseRootOnlyBucketState = true,
                BucketLargePrimeCutoff = int.Parse(
                    separators[1], CultureInfo.InvariantCulture),
                ResieveLargePrimeCutoff = int.Parse(
                    separators[2], CultureInfo.InvariantCulture),
            };
        }
        if (mode.StartsWith("block-", StringComparison.Ordinal))
            return baseline with
            {
                UseRootOnlyBucketState = true,
                SieveBlockSize = int.Parse(
                    mode.AsSpan("block-".Length), CultureInfo.InvariantCulture),
            };

        var legacy = baseline with
        {
            UseCandidateMajorResieve = false,
            UseVectorCandidateMajorResieve = false,
            VectorCandidateMajorMaximumCandidates = int.MaxValue,
            UseVectorMediumPrimeTrialDivision = false,
            UseFlatCandidateOffsetMap = false,
            FlatCandidateOffsetMapMinimumCandidates = int.MaxValue,
            VectorCandidateMajorBucketMaximumCandidates = 0,
            UseGeometryAdaptiveBucketMatching = false,
        };

        if (mode == "promoted") return baseline;
        if (mode is "simd-medium" or "combined16")
        {
            return legacy with
            {
                UseVectorCandidateMajorResieve = true,
                VectorCandidateMajorMaximumCandidates = 16,
                UseVectorMediumPrimeTrialDivision = true,
            };
        }
        if (mode is "adaptive-bucket" or "geometry-adaptive")
        {
            return legacy with
            {
                FlatCandidateOffsetMapMinimumCandidates = 16,
                VectorCandidateMajorBucketMaximumCandidates = 15,
                UseGeometryAdaptiveBucketMatching = true,
            };
        }
        if (mode == "candidate" && !bucketFocus)
            return legacy with { UseCandidateMajorResieve = true };
        if (mode.StartsWith("vector", StringComparison.Ordinal))
        {
            return legacy with
            {
                UseVectorCandidateMajorResieve = true,
                VectorCandidateMajorMaximumCandidates = mode switch
                {
                    "vector4" => 4,
                    "vector8" => 8,
                    "vector16" => 16,
                    _ => int.MaxValue,
                },
            };
        }

        return ConfigureBucketMode(legacy, mode);
    }

    private static SievingParameters ConfigureBucketMode(
        SievingParameters legacy, string mode)
        => legacy with
        {
            FlatCandidateOffsetMapMinimumCandidates = mode switch
            {
                "map-1" => 1,
                "map-3" or "adaptive-1" or "adaptive-2" => 3,
                "hybrid-4" => 4,
                "hybrid-8" => 8,
                "hybrid-16" => 16,
                _ => int.MaxValue,
            },
            VectorCandidateMajorBucketMaximumCandidates = mode switch
            {
                "candidate" => int.MaxValue,
                "adaptive-1" => 1,
                "adaptive-2" => 2,
                "hybrid-4" => 3,
                "hybrid-8" => 7,
                "hybrid-16" => 15,
                _ => 0,
            },
        };

    private static void WriteMeasurement(
        string mode, int threshold, int repetition, Stopwatch watch,
        SievingCounters counters)
    {
        var label = threshold == int.MaxValue
            ? "off"
            : threshold.ToString(CultureInfo.InvariantCulture);
        Console.WriteLine(
            $"{mode},{label},{repetition},{watch.Elapsed.TotalMilliseconds:F3}," +
            $"{counters.KnownHitCollectionCpuMs},{counters.TrialDivCpuMs}," +
            $"{counters.ScanCpuMs},{counters.DirectGatedCandidateBlocks}," +
            $"{counters.ResievedCandidateBlocks},{counters.Polynomials}," +
            $"{counters.Candidates},{counters.RawRelations},{counters.UsableRelations}," +
            $"{counters.BucketBinaryCandidateBlocks}," +
            $"{counters.BucketCandidateMajorBlocks}," +
            $"{counters.BucketOffsetMapBlocks}," +
            $"{counters.BucketCandidateHitInspections}," +
            $"{counters.BucketCandidateVectorGroups}," +
            $"{counters.BucketCandidateMatchingMasks}," +
            $"{counters.BucketOffsetMapProbes}," +
            $"{counters.BucketDecodedPrimeHits}," +
            $"{counters.SieveInitCpuMs},{counters.SieveClearCpuMs}," +
            $"{counters.SmallPrimeFillCpuMs}," +
            $"{counters.DirectFillCpuMs},{counters.BucketScatterCpuMs}," +
            $"{counters.BucketReplayCpuMs}," +
            $"{counters.DirectKnownHitCollectionCpuMs}," +
            $"{counters.BucketKnownHitCollectionCpuMs}," +
            $"{counters.BucketSlabBytesPerWorker},{counters.BucketOverflowHits}," +
            $"{counters.BucketMaximumHitsPerBucket},{counters.BucketCapacityPerBucket}," +
            $"{counters.TrialDivPreCpuMs},{counters.TrialDivPostCpuMs}," +
            $"{counters.TrialDivPostAPosCpuMs},{counters.TrialDivPostCheckCpuMs}," +
            $"{counters.TrialDivPostParityCpuMs}");
    }

    private static int ParseOptional(string[] args, int index, int defaultValue)
        => args.Length > index
            ? int.Parse(args[index], NumberStyles.None, CultureInfo.InvariantCulture)
            : defaultValue;
}
