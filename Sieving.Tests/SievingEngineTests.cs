using System.Numerics;
using System.Runtime.InteropServices;
using Factorbase;
using Filtering;
using Sieving;
using SIQS.Contracts;
using SIQS.Contracts.Files;
using SIQS.Contracts.Numerics;

namespace Sieving.Tests;

public class SievingEngineTests
{
    private static FactorBaseDocument FactorBase(string n, long bound, BigInteger multiplier)
        => FactorBaseGenerator.Generate(new FactorBaseOptions(BigInteger.Parse(n), bound, multiplier)).FactorBase!;

    /// <summary>
    /// Reconstructs sign(V)*A*|V| from the relation's exponent map (column 0 == -1, column c == its
    /// prime) and verifies the SIQS congruence t^2 == that value (mod ScaledN), multiplying in the
    /// large prime for partials. This is the end-to-end correctness invariant for the whole sieve.
    /// </summary>
    private static void AssertRelationValid(RawRelationRecord r, FactorBaseDocument fb)
    {
        var primeByColumn = fb.Entries.ToDictionary(e => e.Index, e => (BigInteger)e.Prime);
        var scaledN = fb.Metadata.ScaledN;

        var value = BigInteger.One;
        foreach (var (column, exponent) in r.FactorExponents)
        {
            var basis = column == 0 ? BigInteger.MinusOne : primeByColumn[column];
            value *= BigInteger.Pow(basis, exponent);
        }

        if (r.LargePrime is { } q)
        {
            value *= q;
        }

        var lhs = BigInteger.ModPow(r.T, 2, scaledN);
        var rhs = IntegerMath.Mod(value, scaledN);
        Assert.Equal(lhs, rhs);

        // parity columns are exactly the odd-exponent columns
        var expectedParity = r.FactorExponents.Where(kv => (kv.Value & 1) == 1).Select(kv => kv.Key).OrderBy(c => c);
        Assert.Equal(expectedParity, r.ParityColumns);
    }

    private static string RelationSignature(RawRelationRecord r)
        => string.Join('|',
            r.RelationId,
            SiqsTokens.ToToken(r.Kind),
            r.PolyId,
            r.A,
            r.B,
            r.C,
            r.X,
            r.T,
            r.Sign,
            string.Join(' ', r.FactorExponents.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}")),
            string.Join(' ', r.ParityColumns),
            r.LargePrime?.ToString() ?? "");

    private static string[] AllRelationSignatures(SievingResult result)
        => result.FullRelations.Concat(result.Partials).Select(RelationSignature).ToArray();

    private static string[] AllRelationSignatures(IEnumerable<RawRelationRecord> records)
        => records.Select(RelationSignature).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    private static FactorBaseDocument WithoutPrime(FactorBaseDocument factorBase, long prime)
    {
        var entries = factorBase.Entries
            .Where(e => e.Prime != prime)
            .OrderBy(e => e.Prime)
            .Select((e, i) => e with { Index = i + 1 })
            .ToArray();

        return factorBase with { Entries = entries };
    }

    private sealed class CapturingProgress : IProgress<SiqsProgressEvent>
    {
        public List<SiqsProgressEvent> Events { get; } = [];

        public void Report(SiqsProgressEvent value) => Events.Add(value);
    }

    private static ulong NextUInt64(Random random)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        random.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private static ulong ReferencePowMod64(ulong value, ulong exponent, ulong modulus)
    {
        var result = 1UL % modulus;
        var b = value % modulus;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                result = (ulong)((UInt128)result * b % modulus);
            }

            exponent >>= 1;
            if (exponent != 0)
            {
                b = (ulong)((UInt128)b * b % modulus);
            }
        }

        return result;
    }

    private static bool TrialDivisionPrime(ulong n)
    {
        if (n < 2)
        {
            return false;
        }

        if ((n & 1) == 0)
        {
            return n == 2;
        }

        for (var d = 3UL; d * d <= n; d += 2)
        {
            if (n % d == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<int, int> FixedAExponents(BigInteger a, IReadOnlyDictionary<int, long> primeByColumn)
    {
        var result = new Dictionary<int, int>();
        foreach (var (column, prime) in primeByColumn)
        {
            var exponent = 0;
            while (a % prime == 0)
            {
                a /= prime;
                exponent++;
            }

            if (exponent > 0)
            {
                result[column] = exponent;
            }
        }

        return result;
    }

    private static SievingParameters SmallDeterministicParameters(FactorBaseDocument fb)
        => SievingParameters.Default(fb) with
        {
            SieveHalfInterval = 20_000,
            APrimeCount = 2,
            APrimeWindowSize = 24,
            ErrorMargin = 20,
            RelationTarget = 40,
            PolynomialCount = 5_000,
            Parallelism = 1,
        };

    [Fact]
    public void All_emitted_relations_satisfy_the_congruence()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SievingParameters.Default(fb) with
        {
            SieveHalfInterval = 20_000,
            APrimeCount = 2,
            APrimeWindowSize = 24,
            ErrorMargin = 20,
            RelationTarget = 40,
            PolynomialCount = 5_000,
        };

        var result = SievingEngine.Sieve(fb, parameters);

        Assert.NotEmpty(result.FullRelations);
        foreach (var r in result.FullRelations)
        {
            Assert.Equal(RelationKind.Full, r.Kind);
            Assert.Null(r.LargePrime);
            AssertRelationValid(r, fb);
        }

        foreach (var r in result.Partials)
        {
            Assert.Equal(RelationKind.Partial, r.Kind);
            Assert.NotNull(r.LargePrime);
            Assert.True(r.LargePrime!.Value > fb.Metadata.Bound);
            Assert.True(r.LargePrime!.Value <= parameters.LargePrimeBound);
            AssertRelationValid(r, fb);
        }
    }

    [Fact]
    public void Relation_ids_are_unique()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SievingParameters.Default(fb) with
        {
            SieveHalfInterval = 20_000, APrimeCount = 2, APrimeWindowSize = 24,
            ErrorMargin = 20, RelationTarget = 40, PolynomialCount = 5_000,
        };

        var result = SievingEngine.Sieve(fb, parameters);

        var allIds = result.FullRelations.Concat(result.Partials).Select(r => r.RelationId).ToList();
        Assert.Equal(allIds.Count, allIds.Distinct().Count());
    }

    [Fact]
    public void Relation_ids_are_deterministic_with_single_thread()
    {
        // With Parallelism=1 the sieve is sequential so output is byte-for-byte stable across runs.
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb);

        var first = SievingEngine.Sieve(fb, parameters);
        var second = SievingEngine.Sieve(fb, parameters);

        Assert.Equal(AllRelationSignatures(first), AllRelationSignatures(second));
    }

    [Fact]
    public void Disable_vector_scan_runs_scalar_candidate_scan()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb) with
        {
            SieveBlockSize = 4_096,
            DisableVectorScan = true,
        };

        var result = SievingEngine.Sieve(fb, parameters);
        var allRelations = result.FullRelations.Concat(result.Partials).ToArray();

        Assert.NotEmpty(allRelations);
        Assert.All(allRelations, r => AssertRelationValid(r, fb));
    }

    [Fact]
    public void Vector_and_scalar_candidate_scan_produce_same_single_thread_relation_output()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var vectorScan = SmallDeterministicParameters(fb) with { SieveBlockSize = 4_096 };
        var scalarScan = vectorScan with { DisableVectorScan = true };

        var vectorResult = SievingEngine.Sieve(fb, vectorScan);
        var scalarResult = SievingEngine.Sieve(fb, scalarScan);

        Assert.Equal(AllRelationSignatures(vectorResult), AllRelationSignatures(scalarResult));
    }

    [Fact]
    public void Small_prime_variation_recovers_valid_relations_and_rejects_provisional_reports()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb) with
        {
            SieveBlockSize = 4_096,
            SmallPrimeVariationBound = 256,
        };

        var result = SievingEngine.Sieve(fb, parameters);
        var relations = result.FullRelations.Concat(result.Partials).ToArray();

        Assert.NotEmpty(relations);
        Assert.All(relations, relation => AssertRelationValid(relation, fb));
        Assert.True(result.Counters.SmallPrimeVariationCount > 0);
        Assert.True(result.Counters.SmallPrimeVariationAllowance > 0);
        Assert.True(result.Counters.SmallPrimeVariationReports > result.Counters.Candidates);
        Assert.True(result.Counters.SmallPrimeVariationRejected > 0);
        Assert.True(result.Counters.SmallPrimeVariationCpuMs >= 0);
    }

    [Fact]
    public void Vector_and_modulo_gray_root_updates_produce_same_single_thread_relation_output()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var vectorRoots = SmallDeterministicParameters(fb) with
        {
            SieveBlockSize = 4_096,
            RelationTarget = int.MaxValue,
            PolynomialCount = 32,
        };
        var moduloRoots = vectorRoots with { DisableVectorRootUpdate = true };

        var vectorResult = SievingEngine.Sieve(fb, vectorRoots);
        var moduloResult = SievingEngine.Sieve(fb, moduloRoots);

        Assert.Equal(AllRelationSignatures(vectorResult), AllRelationSignatures(moduloResult));
    }

    [Fact]
    public void Banded_and_generic_direct_sieve_produce_same_single_thread_relation_output()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var banded = SmallDeterministicParameters(fb) with { SieveBlockSize = 256 };
        var generic = banded with { DisableBandedDirectSieve = true };

        var bandedResult = SievingEngine.Sieve(fb, banded);
        var genericResult = SievingEngine.Sieve(fb, generic);

        Assert.Equal(AllRelationSignatures(genericResult), AllRelationSignatures(bandedResult));
    }

    [Fact]
    public void Resume_state_skips_completed_a_indices_and_preserves_relation_set()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb) with
        {
            RelationTarget = int.MaxValue,
            PolynomialCount = 6,
            Parallelism = 1,
        };

        var baseline = SievingEngine.Sieve(fb, parameters);
        Assert.NotEmpty(baseline.FullRelations.Concat(baseline.Partials));

        var existing = baseline.FullRelations.Concat(baseline.Partials)
            .Where(r => r.RelationId.StartsWith("R000000_", StringComparison.Ordinal))
            .ToArray();
        var sink = new InMemoryRawRelationSink();
        var resumeState = new SievingResumeState(new HashSet<int> { 0 }, existing);

        SievingEngine.Sieve(fb, parameters, sink, progress: null, CancellationToken.None, resumeState);

        var expected = AllRelationSignatures(baseline.FullRelations.Concat(baseline.Partials));
        var actual = AllRelationSignatures(existing.Concat(sink.FullRelations).Concat(sink.Partials));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Montgomery64_matches_uint128_reference_for_arithmetic()
    {
        ulong[] moduli =
        [
            3,
            5,
            17,
            4_294_967_291,
            (1UL << 63) + 1,
            18_446_744_073_709_551_557,
            ulong.MaxValue,
        ];
        ulong[] corners = [0, 1, 2, 3, 17, ulong.MaxValue - 1, ulong.MaxValue];
        var random = new Random(0x5eed);

        foreach (var n in moduli)
        {
            var montgomery = Montgomery64.Create(n);
            foreach (var a in corners)
            foreach (var b in corners)
            {
                AssertMontgomeryArithmetic(montgomery, a, b, exponent: b);
            }

            for (var i = 0; i < 2_000; i++)
            {
                AssertMontgomeryArithmetic(montgomery, NextUInt64(random), NextUInt64(random), NextUInt64(random));
            }
        }
    }

    private static void AssertMontgomeryArithmetic(Montgomery64 montgomery, ulong a, ulong b, ulong exponent)
    {
        var n = montgomery.N;
        var aMod = a % n;
        var bMod = b % n;
        var aMontgomery = montgomery.ToMontgomery(a);
        var bMontgomery = montgomery.ToMontgomery(b);

        Assert.Equal(
            (ulong)((UInt128)aMod * bMod % n),
            montgomery.FromMontgomery(montgomery.Multiply(aMontgomery, bMontgomery)));
        Assert.Equal(
            (ulong)(((UInt128)aMod + bMod) % n),
            montgomery.FromMontgomery(montgomery.Add(aMontgomery, bMontgomery)));
        Assert.Equal(ReferencePowMod64(a, exponent, n), montgomery.PowMod(a, exponent));
    }

    [Fact]
    public void Montgomery64_rejects_even_or_too_small_moduli()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Montgomery64.Create(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Montgomery64.Create(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Montgomery64.Create(4));
    }

    [Fact]
    public void IsPrime64_matches_trial_division_for_small_values_and_composite_witnesses()
    {
        for (var n = 0UL; n < 100_000; n++)
        {
            Assert.Equal(TrialDivisionPrime(n), CofactorPrimality64.IsPrime(n));
        }

        ulong[] compositeWitnessValues = [325, 9_375, 28_178, 450_775, 9_780_504, 1_795_265_022];
        foreach (var n in compositeWitnessValues)
        {
            Assert.False(CofactorPrimality64.IsPrime(n));
        }

        Assert.True(CofactorPrimality64.IsPrime(2_147_483_647));
        Assert.True(CofactorPrimality64.IsPrime(4_294_967_291));
        Assert.True(CofactorPrimality64.IsPrime(18_446_744_073_709_551_557));
        Assert.False(CofactorPrimality64.IsPrime(3_215_031_751));
    }

    [Fact]
    public void Base2_probable_prime_screen_uses_strong_test()
    {
        Assert.False(CofactorPrimality64.IsBase2ProbablePrime(341));
        Assert.True(CofactorPrimality64.IsBase2ProbablePrime(2_147_483_647));
    }

    [Fact]
    public void Batch_file_sink_resumes_numbering_after_existing_batches()
    {
        var dir = Directory.CreateTempSubdirectory("siqs-sink-tests-").FullName;
        try
        {
            var metadata = new RawRelationsMetadata(77, 1, 77, 2, 128);
            File.WriteAllText(Path.Combine(dir, "relations_0007.txt"),
                RawRelationsFile.Write(new RawRelationsDocument(FileFormats.RawRelationsV1, metadata, Array.Empty<RawRelationRecord>())));
            var sink = new RawRelationBatchFileSink(dir, metadata, batchSize: 1);

            sink.Add(new RawRelationRecord(
                "R000008", RelationKind.Full, "P000000",
                A: 1, B: 0, C: -77, X: 1, T: 1, Sign: 1,
                FactorExponents: new Dictionary<int, int>(),
                ParityColumns: Array.Empty<int>(),
                LargePrime: null));
            sink.Complete();

            Assert.True(File.Exists(Path.Combine(dir, "relations_0008.txt")));
            Assert.False(File.Exists(Path.Combine(dir, "relations_0000.txt")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Batch_file_sink_flushes_durably_without_fragmenting_active_batch()
    {
        var dir = Directory.CreateTempSubdirectory("siqs-sink-tests-").FullName;
        try
        {
            var metadata = new RawRelationsMetadata(77, 1, 77, 2, 128);
            var sink = new RawRelationBatchFileSink(dir, metadata, batchSize: 3);

            sink.Add(FullRelation("R000000"));
            sink.Flush();

            var firstPath = Path.Combine(dir, "relations_0000.txt");
            Assert.Single(RawRelationsFile.Parse(File.ReadAllText(firstPath)).Relations);

            sink.Add(FullRelation("R000001"));
            sink.Flush();
            sink.Add(FullRelation("R000002"));
            sink.Flush();
            sink.Add(FullRelation("R000003"));
            sink.Complete();

            var batches = Directory.GetFiles(dir, "relations_*.txt")
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(2, batches.Length);
            Assert.Equal(
                new[] { "R000000", "R000001", "R000002" },
                RawRelationsFile.Parse(File.ReadAllText(batches[0])).Relations.Select(r => r.RelationId));
            Assert.Equal(
                new[] { "R000003" },
                RawRelationsFile.Parse(File.ReadAllText(batches[1])).Relations.Select(r => r.RelationId));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Batch_file_sink_resumes_both_streams_after_five_digit_batches()
    {
        var dir = Directory.CreateTempSubdirectory("siqs-sink-tests-").FullName;
        try
        {
            var metadata = new RawRelationsMetadata(77, 1, 77, 2, 128);
            File.WriteAllText(
                Path.Combine(dir, "relations_10000.txt"),
                RawRelationsFile.Write(new RawRelationsDocument(
                    FileFormats.RawRelationsV1, metadata, Array.Empty<RawRelationRecord>())));
            File.WriteAllText(
                Path.Combine(dir, "partials_10000.txt"),
                RawRelationsFile.Write(new RawRelationsDocument(
                    FileFormats.RawPartialsV1, metadata, Array.Empty<RawRelationRecord>())));
            var sink = new RawRelationBatchFileSink(dir, metadata, batchSize: 1);

            sink.Add(new RawRelationRecord(
                "R-full", RelationKind.Full, "P000000",
                A: 1, B: 0, C: -77, X: 1, T: 1, Sign: 1,
                FactorExponents: new Dictionary<int, int>(),
                ParityColumns: Array.Empty<int>(),
                LargePrime: null));
            sink.Add(new RawRelationRecord(
                "R-partial", RelationKind.Partial, "P000000",
                A: 1, B: 0, C: -77, X: 1, T: 1, Sign: 1,
                FactorExponents: new Dictionary<int, int>(),
                ParityColumns: Array.Empty<int>(),
                LargePrime: 101));
            sink.Complete();

            Assert.True(File.Exists(Path.Combine(dir, "relations_10001.txt")));
            Assert.True(File.Exists(Path.Combine(dir, "partials_10001.txt")));
            Assert.Contains("relations_10000.txt", sink.Artifacts);
            Assert.Contains("partials_10000.txt", sink.Artifacts);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Explicit_block_size_does_not_change_single_thread_relation_output()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var baseline = SmallDeterministicParameters(fb) with { SieveBlockSize = 0 };
        var smallBlocks = baseline with { SieveBlockSize = 4_096 };

        var first = SievingEngine.Sieve(fb, baseline);
        var second = SievingEngine.Sieve(fb, smallBlocks);

        Assert.Equal(AllRelationSignatures(first), AllRelationSignatures(second));
    }

    private static RawRelationRecord FullRelation(string relationId)
        => new(
            relationId, RelationKind.Full, "P000000",
            A: 1, B: 0, C: -77, X: 1, T: 1, Sign: 1,
            FactorExponents: new Dictionary<int, int>(),
            ParityColumns: Array.Empty<int>(),
            LargePrime: null);

    [Fact]
    public void Bucket_large_prime_cutoff_does_not_change_single_thread_relation_output()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var baseline = SmallDeterministicParameters(fb) with { BucketLargePrimeCutoff = 0 };
        var bucketed = baseline with { BucketLargePrimeCutoff = 101 };

        var first = SievingEngine.Sieve(fb, baseline);
        var second = SievingEngine.Sieve(fb, bucketed);

        Assert.Equal(AllRelationSignatures(first), AllRelationSignatures(second));
    }

    [Fact]
    public void Resieve_cutoff_does_not_change_single_thread_relation_output()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var baseline = SmallDeterministicParameters(fb) with
        {
            SieveBlockSize = 4_096,
            BucketLargePrimeCutoff = 0,
            ResieveLargePrimeCutoff = 0,
            RelationTarget = int.MaxValue,
            PolynomialCount = 16,
        };
        SievingParameters[] equivalentModes =
        [
            baseline with { ResieveLargePrimeCutoff = 3 },
            baseline with { BucketLargePrimeCutoff = 401 },
            baseline with
            {
                BucketLargePrimeCutoff = 401,
                ResieveLargePrimeCutoff = 101,
                CandidateResieveMinimumCandidates = 1,
            },
            baseline with { BucketLargePrimeCutoff = 401, ResieveLargePrimeCutoff = 101 },
            baseline with { BucketLargePrimeCutoff = 401, ResieveLargePrimeCutoff = 401 },
            baseline with { BucketLargePrimeCutoff = 401, ResieveLargePrimeCutoff = 997 },
            baseline with
            {
                BucketLargePrimeCutoff = 401,
                ResieveLargePrimeCutoff = 101,
                UseCandidateMajorResieve = true,
            },
            baseline with
            {
                BucketLargePrimeCutoff = 401,
                ResieveLargePrimeCutoff = 101,
                UseFlatCandidateOffsetMap = true,
            },
            baseline with
            {
                BucketLargePrimeCutoff = 401,
                ResieveLargePrimeCutoff = 101,
                UseVectorCandidateMajorResieve = true,
            },
            baseline with
            {
                BucketLargePrimeCutoff = 401,
                ResieveLargePrimeCutoff = 101,
                UseVectorCandidateMajorResieve = true,
                UseContiguousVectorResieveLoads = true,
            },
            baseline with
            {
                BucketLargePrimeCutoff = 401,
                ResieveLargePrimeCutoff = 101,
                UseRootOnlyBucketState = false,
            },
            baseline with
            {
                BucketLargePrimeCutoff = 401,
                ResieveLargePrimeCutoff = 101,
                UseVectorCandidateMajorResieve = true,
                UseVectorMediumPrimeTrialDivision = true,
            },
            baseline with
            {
                BucketLargePrimeCutoff = 401,
                ResieveLargePrimeCutoff = 101,
                VectorCandidateMajorBucketMaximumCandidates = 2,
                FlatCandidateOffsetMapMinimumCandidates = 3,
            },
            baseline with
            {
                BucketLargePrimeCutoff = 401,
                ResieveLargePrimeCutoff = 101,
                UseVectorCandidateMajorResieve = false,
                UseVectorMediumPrimeTrialDivision = false,
                UseGeometryAdaptiveBucketMatching = false,
                VectorCandidateMajorBucketMaximumCandidates = 0,
                FlatCandidateOffsetMapMinimumCandidates = int.MaxValue,
            },
        ];

        var first = SievingEngine.Sieve(fb, baseline);
        var expected = AllRelationSignatures(first.FullRelations.Concat(first.Partials));
        Assert.NotEmpty(expected);

        foreach (var mode in equivalentModes)
        {
            var result = SievingEngine.Sieve(fb, mode);
            Assert.Equal(expected, AllRelationSignatures(result.FullRelations.Concat(result.Partials)));
        }
    }

    [Fact]
    public void Large_prime_bucket_overflow_spill_replays_hits()
    {
        var buckets = new LargePrimeBuckets(new(new(1), new(1), new(16)), assertOnOverflow: false);
        buckets.Add(new(0), new(3), new(7), new(5));
        buckets.Add(new(0), new(3), new(11), new(9));

        var sieve = new byte[16];
        ref var sieve0 = ref MemoryMarshal.GetArrayDataReference(sieve);
        buckets.PrepareBlock(new(0), ref sieve0);

        Assert.Equal(14, sieve[3]);
        Assert.Equal(1, buckets.OverflowHitCount);
        Assert.Equal(2, buckets.MaximumHitCount);
        var spill = Assert.Single(buckets.OverflowAt(new(0))!);
        Assert.Equal(3, spill.Offset.Value);
        Assert.Equal(11, spill.PrimeIndex.Value);
        Assert.Equal(9, spill.LogCredit.Value);
    }

    [Fact]
    public void Large_prime_bucket_candidate_recovery_matches_offsets_including_spill()
    {
        var buckets = new LargePrimeBuckets(new(new(1), new(2), new(16)), assertOnOverflow: false);
        buckets.Add(new(0), new(3), new(7), new(5));
        buckets.Add(new(0), new(9), new(8), new(6));
        buckets.Add(new(0), new(3), new(11), new(9)); // spills past capacity

        var primeHits = new List<int>[] { [], [] };
        buckets.CollectCandidateHits(new(0), candidateOffsets: [3, 12], primeHits);

        Assert.Equal(new[] { 7, 11 }, primeHits[0]);
        Assert.Empty(primeHits[1]);
    }

    [Fact]
    public void Vector_candidate_major_bucket_matching_covers_lanes_tail_and_spill()
    {
        var buckets = new LargePrimeBuckets(
            new(new(1), new(10), new(32)), assertOnOverflow: false);
        for (var i = 0; i < 10; i++)
            buckets.Add(new(0), new(i % 3), new(100 + i), new(5));
        buckets.Add(new(0), new(2), new(999), new(5));

        var expected = new List<int>[] { [], [], [] };
        var actual = new List<int>[] { [], [], [] };
        buckets.CollectCandidateHits(new(0), [0, 1, 2], expected);
        buckets.CollectCandidateHitsCandidateMajorVector(new(0), [0, 1, 2], actual);

        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i]);
    }

    [Fact]
    public void Small_block_size_relations_satisfy_congruence_and_bounds()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb) with { SieveBlockSize = 4_096 };

        var result = SievingEngine.Sieve(fb, parameters);

        Assert.NotEmpty(result.FullRelations);
        Assert.NotEmpty(result.Partials);
        Assert.Equal(result.FullRelations.Count + result.Partials.Count,
            result.FullRelations.Concat(result.Partials).Select(r => r.RelationId).Distinct().Count());

        foreach (var relation in result.FullRelations.Concat(result.Partials))
        {
            AssertRelationValid(relation, fb);
            if (relation.Kind == RelationKind.Partial)
            {
                Assert.NotNull(relation.LargePrime);
                Assert.True(relation.LargePrime!.Value > fb.Metadata.Bound);
                Assert.True(relation.LargePrime!.Value <= parameters.LargePrimeBound);
            }
        }
    }

    [Fact]
    public void Diagnostic_counters_are_reported_as_non_negative_values()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb) with { SieveBlockSize = 4_096 };

        var result = SievingEngine.Sieve(fb, parameters);

        Assert.True(result.Counters.Candidates >= result.Counters.FullRelations + result.Counters.Partials);
        Assert.True(result.Counters.Blocks > 0);
        Assert.True(result.Counters.UsableRelations >= 0);
        Assert.True(result.Counters.UsablePartialPairs >= 0);
        Assert.True(result.Counters.ZeroParityFullRelations >= 0);
        Assert.True(result.Counters.ZeroParityPartialPairs >= 0);
        Assert.Equal(result.Counters.UsableRelations, result.Counters.ProjectedMatrixRows);
        Assert.Equal(fb.Entries.Count + 1, result.Counters.ProjectedMatrixColumns);
        Assert.True(result.Counters.Discarded >= 0);
        Assert.True(result.Counters.SetupCpuMs >= 0);
        Assert.True(result.Counters.SieveFillCpuMs >= 0);
        Assert.True(result.Counters.SieveInitCpuMs >= 0);
        Assert.True(result.Counters.ScanCpuMs >= 0);
        Assert.True(result.Counters.PolyEvalCpuMs >= 0);
        Assert.True(result.Counters.TrialDivCpuMs >= 0);
        Assert.True(result.Counters.TrialDivPreCpuMs >= 0);
        Assert.True(result.Counters.TrialDivPostCpuMs >= 0);
        Assert.True(result.Counters.TrialDivPostAPosCpuMs >= 0);
        Assert.True(result.Counters.TrialDivPostCheckCpuMs >= 0);
        Assert.True(result.Counters.TrialDivPostParityCpuMs >= 0);
        Assert.True(result.Counters.BucketOverflowHits >= 0);
        Assert.True(result.Counters.BucketSlabBytesPerWorker >= 0);
        Assert.True(result.Counters.BucketMaximumHitsPerBucket >= 0);
        Assert.True(result.Counters.BucketCapacityPerBucket >= 0);
    }

    [Fact]
    public void Bucket_and_candidate_density_counters_are_reported_in_progress()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb) with
        {
            SieveBlockSize = 4_096,
            BucketLargePrimeCutoff = 101,
        };
        var progress = new CapturingProgress();

        var result = SievingEngine.Sieve(fb, parameters, progress);

        Assert.Equal(0, result.Counters.BucketOverflowHits);
        Assert.True(result.Counters.BucketSlabBytesPerWorker > 0);
        Assert.True(result.Counters.BucketMaximumHitsPerBucket > 0);
        Assert.True(result.Counters.BucketCapacityPerBucket
            >= result.Counters.BucketMaximumHitsPerBucket);
        var final = Assert.Single(progress.Events, e => e.Message == "sieving complete");
        Assert.Equal("0", final.Counters["bucket_overflow_hits"]);
        Assert.True(long.Parse(final.Counters["bucket_slab_bytes_per_worker"]) > 0);
        Assert.True(long.Parse(final.Counters["bucket_maximum_hits_per_bucket"]) > 0);
        Assert.True(long.Parse(final.Counters["bucket_capacity_per_bucket"])
            >= long.Parse(final.Counters["bucket_maximum_hits_per_bucket"]));
        Assert.True(long.Parse(final.Counters["blocks"]) > 0);
        Assert.Contains("candidates_per_block", final.Counters.Keys);
    }

    [Fact]
    public void Parallel_sieve_outputs_valid_unique_relations()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb) with
        {
            Parallelism = 2,
            SieveBlockSize = 4_096,
        };

        var result = SievingEngine.Sieve(fb, parameters);
        var allRelations = result.FullRelations.Concat(result.Partials).ToArray();

        Assert.NotEmpty(allRelations);
        Assert.Equal(allRelations.Length, allRelations.Select(r => r.RelationId).Distinct().Count());
        Assert.All(allRelations, r => AssertRelationValid(r, fb));
    }

    [Fact]
    public void Sieve_emits_relations_when_selected_a_prime_also_divides_v()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb);

        var result = SievingEngine.Sieve(fb, parameters);
        var primeByColumn = fb.Entries.ToDictionary(e => e.Index, e => e.Prime);
        var allRelations = result.FullRelations.Concat(result.Partials).ToArray();

        Assert.Contains(allRelations, relation =>
            FixedAExponents(relation.A, primeByColumn).Any(kv =>
                relation.FactorExponents.TryGetValue(kv.Key, out var exponent) && exponent > kv.Value));
    }

    [Fact]
    public void Multiplier_prime_column_matches_the_single_t_root()
    {
        var fb = FactorBase("1022117", 1000, 3);
        var multiplierPrime = fb.Entries.Single(e => e.Prime == 3);
        var parameters = SmallDeterministicParameters(fb) with
        {
            RelationTarget = int.MaxValue,
            PolynomialCount = 64,
            Parallelism = 1,
        };

        var result = SievingEngine.Sieve(fb, parameters);
        var relations = result.FullRelations.Concat(result.Partials).ToArray();

        Assert.NotEmpty(relations);
        Assert.Contains(relations, r => r.FactorExponents.ContainsKey(multiplierPrime.Index));
        foreach (var relation in relations)
        {
            var hasMultiplierPrime = relation.FactorExponents.TryGetValue(multiplierPrime.Index, out var exponent);
            Assert.Equal(hasMultiplierPrime, relation.T % multiplierPrime.Prime == 0);
            if (hasMultiplierPrime)
            {
                Assert.Equal(1, exponent);
            }
        }
    }

    [Fact]
    public void Including_multiplier_prime_recovers_relations_and_preserves_declared_parity()
    {
        var fb = FactorBase("1022117", 1000, 3);
        var legacy = WithoutPrime(fb, 3);
        var parameters = SmallDeterministicParameters(fb) with
        {
            RelationTarget = int.MaxValue,
            PolynomialCount = 128,
            Parallelism = 1,
        };

        var before = SievingEngine.Sieve(legacy, parameters);
        var after = SievingEngine.Sieve(fb, parameters);

        Assert.True(
            after.FullRelations.Count > before.FullRelations.Count,
            $"Expected full relation recovery, before={before.FullRelations.Count}, after={after.FullRelations.Count}.");
        FilteringEngine.Run(fb, after.FullRelations, after.Partials);
    }

    [Fact]
    public void Trial_spread_order_samples_across_a_candidate_range()
    {
        var order = SieveRunCoordinator.TrialSpreadOrder(20);

        Assert.Equal(20, order.Count);
        Assert.Equal(Enumerable.Range(0, 20), order.Order());
        Assert.Equal(new[] { 0, 19, 9, 4, 14 }, order.Take(5));
    }

    [Fact]
    public void Throws_when_no_a_candidates_available()
    {
        // Factor base with too few eligible primes for the requested A prime count.
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SievingParameters.Default(fb) with { APrimeCount = 999 };
        Assert.Throws<InvalidOperationException>(() => SievingEngine.Sieve(fb, parameters));
    }

    [Fact]
    public void Candidate_threshold_allows_one_large_prime_cofactor()
    {
        var fullSmoothThreshold = 10.0 * Math.Log(1_000_000.0) - 24;
        var threshold = SievingEngine.CandidateThreshold(
            logScale: 10.0,
            vEstimate: 1_000_000.0,
            largePrimeLogAllowance: 10.0 * Math.Log(10_000.0),
            errorMargin: 24);

        Assert.True(threshold < fullSmoothThreshold);
        Assert.Equal(fullSmoothThreshold - 10.0 * Math.Log(10_000.0), threshold, precision: 10);
    }

    [Fact]
    public void Candidate_threshold_can_use_smaller_two_large_prime_threshold_bound()
    {
        var estimate = 1.0e30;
        var logScale = 10.0;
        var errorMargin = 20;
        var actualLp2Allowance = logScale * Math.Log(5_000_000.0 * 5_000_000.0);
        var thresholdLp2Allowance = logScale * Math.Log(2_000_000.0 * 2_000_000.0);

        var actualBoundThreshold = SievingEngine.CandidateThreshold(
            logScale,
            estimate,
            actualLp2Allowance,
            errorMargin);
        var thresholdBoundThreshold = SievingEngine.CandidateThreshold(
            logScale,
            estimate,
            thresholdLp2Allowance,
            errorMargin);

        Assert.True(thresholdBoundThreshold > actualBoundThreshold);
        Assert.Equal(2.0 * logScale * Math.Log(5_000_000.0 / 2_000_000.0),
            thresholdBoundThreshold - actualBoundThreshold,
            precision: 10);
    }

    [Fact]
    public void Selected_a_primes_that_also_divide_v_are_removed_from_the_remaining_cofactor()
    {
        var fb = new FactorBaseData
        {
            Count = 4,
            Primes = [2, 3, 5, 7],
            Columns = [1, 2, 3, 4],
            Root1 = [0, 1, 0, 1],
            Root2 = [0, 2, 0, 6],
            LogP = [1, 2, 3, 4],
            PrimeInverses = [0, 0, 0, 0],
            PrimeDivThresholds = [0, 0, 0, 0],
            TargetN = 1,
            Multiplier = 1,
            ScaledN = 1,
            Bound = 7,
            LogScale = 1.0,
        };
        var exponents = new Dictionary<int, int>();
        var rem = new BigInteger(5 * 5 * 5 * 11);
        var remU = (ulong)rem;
        var isSmall = true;

        RelationBuilder.DivideSelectedAPrimeFactors(fb, [2], exponents, ref rem, ref remU, ref isSmall);

        Assert.Equal(new BigInteger(11), rem);
        Assert.True(isSmall);
        Assert.Equal(11UL, remU);
        Assert.Equal(3, exponents[3]);
    }

    [Fact]
    public void Two_large_prime_split_accepts_two_primes_inside_lp2_bound()
    {
        var value = new BigInteger(1_250_003L) * 4_999_999L;

        Assert.True(TwoLargePrimeSplitter.TrySplit(
            value,
            factorBaseBound: 1_188_237,
            largePrime2Bound: 5_000_000,
            out var q1,
            out var q2));
        Assert.Equal(value, q1 * q2);
        Assert.True(q1 > 1_188_237);
        Assert.True(q2 > 1_188_237);
        Assert.True(q1 <= 5_000_000);
        Assert.True(q2 <= 5_000_000);
    }

    [Fact]
    public void Small_factor_prefilter_respects_factor_base_bound()
    {
        Assert.True(CofactorPrimality64.HasSmallFactorAtOrBelow(127UL * 1_000_003UL, 127));
        Assert.False(CofactorPrimality64.HasSmallFactorAtOrBelow(127UL * 1_000_003UL, 126));
        Assert.False(CofactorPrimality64.HasSmallFactorAtOrBelow(131UL * 1_000_003UL, 127));

        var validTinyResidual = new BigInteger(131) * 137;
        Assert.True(TwoLargePrimeSplitter.TrySplit(
            validTinyResidual,
            factorBaseBound: 127,
            largePrime2Bound: 200,
            splitter: CofactorSplitterKind.SqufofRho,
            out var q1,
            out var q2));
        Assert.Equal(validTinyResidual, q1 * q2);
    }

    [Theory]
    [InlineData(CofactorSplitterKind.Squfof)]
    [InlineData(CofactorSplitterKind.MicroEcmSqufof)]
    [InlineData(CofactorSplitterKind.MicroEcmStage2)]
    [InlineData(CofactorSplitterKind.SqufofRho)]
    public void Two_large_prime_splitter_modes_accept_two_primes_inside_lp2_bound(CofactorSplitterKind splitter)
    {
        var value = new BigInteger(1_250_003L) * 4_999_999L;

        Assert.True(TwoLargePrimeSplitter.TrySplit(
            value,
            factorBaseBound: 1_188_237,
            largePrime2Bound: 5_000_000,
            splitter,
            out var q1,
            out var q2));
        Assert.Equal(value, q1 * q2);
    }

    [Fact]
    public void Two_large_prime_split_rejects_prime_residual()
    {
        Assert.False(TwoLargePrimeSplitter.TrySplit(
            4_999_999,
            factorBaseBound: 1_188_237,
            largePrime2Bound: 5_000_000,
            out _,
            out _));
    }

    [Fact]
    public void Two_large_prime_split_rejects_residual_too_small_for_two_large_primes()
    {
        var factorBaseBound = 1_188_237;
        var value = new BigInteger(1_000_003L) * 1_000_033L;

        Assert.True(value <= new BigInteger(factorBaseBound) * factorBaseBound);
        Assert.False(TwoLargePrimeSplitter.TrySplit(
            value,
            factorBaseBound,
            largePrime2Bound: 5_000_000,
            out _,
            out _));
    }

    [Fact]
    public void Two_large_prime_split_rejects_factor_above_lp2_bound()
    {
        var value = new BigInteger(1_250_003L) * 5_000_011L;

        Assert.False(TwoLargePrimeSplitter.TrySplit(
            value,
            factorBaseBound: 1_188_237,
            largePrime2Bound: 5_000_000,
            out _,
            out _));
    }

    [Fact]
    public void Slice_sieve_partitions_reproduce_the_full_assignment()
    {
        // Distributed sieving hands each client a disjoint slice of the A-index space. Sieving two
        // complementary slices must reproduce exactly the relations of sieving the whole space, and
        // must match an exhaustive (target-uncapped) non-slice run — proving slices lose and
        // duplicate nothing, and that per-A-index output is independent of the assignment it lives in.
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb) with { RelationTarget = int.MaxValue, PolynomialCount = long.MaxValue };
        var aCount = PolynomialGenerator.SelectAPositions(FactorBaseData.From(fb), parameters).Count;
        Assert.True(aCount >= 2);

        var mid = aCount / 2;
        var lower = new HashSet<int>(Enumerable.Range(0, mid));
        var upper = new HashSet<int>(Enumerable.Range(mid, aCount - mid));
        var whole = new HashSet<int>(Enumerable.Range(0, aCount));

        var lowerSink = new InMemoryRawRelationSink();
        var upperSink = new InMemoryRawRelationSink();
        var wholeSink = new InMemoryRawRelationSink();
        SievingEngine.Sieve(fb, parameters, lowerSink, null, CancellationToken.None, null, lower);
        SievingEngine.Sieve(fb, parameters, upperSink, null, CancellationToken.None, null, upper);
        SievingEngine.Sieve(fb, parameters, wholeSink, null, CancellationToken.None, null, whole);

        var exhaustive = SievingEngine.Sieve(fb, parameters);

        var expected = AllRelationSignatures(exhaustive.FullRelations.Concat(exhaustive.Partials));
        Assert.NotEmpty(expected);
        Assert.Equal(expected, AllRelationSignatures(wholeSink.FullRelations.Concat(wholeSink.Partials)));
        Assert.Equal(expected, AllRelationSignatures(
            lowerSink.FullRelations.Concat(lowerSink.Partials).Concat(upperSink.FullRelations).Concat(upperSink.Partials)));
    }

    [Fact]
    public void Slice_sieve_rejects_an_out_of_range_a_index()
    {
        var fb = FactorBase("1022117", 1000, 1);
        var parameters = SmallDeterministicParameters(fb);
        var aCount = PolynomialGenerator.SelectAPositions(FactorBaseData.From(fb), parameters).Count;

        Assert.Throws<InvalidOperationException>(() => SievingEngine.Sieve(
            fb, parameters, new InMemoryRawRelationSink(), null, CancellationToken.None, null,
            new HashSet<int> { aCount }));
    }

    [Fact]
    public void C52_sample_emits_single_large_prime_partials_with_default_slack()
    {
        var fb = FactorBase("4941549382258631167910462488543523370646879959827479", 63_001, 19);
        var parameters = SievingParameters.Default(fb) with
        {
            PolynomialCount = 32,
            RelationTarget = int.MaxValue,
        };

        var result = SievingEngine.Sieve(fb, parameters);

        Assert.NotEmpty(result.Partials);
        Assert.All(result.Partials, r =>
        {
            Assert.Equal(RelationKind.Partial, r.Kind);
            Assert.NotNull(r.LargePrime);
            Assert.True(r.LargePrime!.Value > fb.Metadata.Bound);
            Assert.True(r.LargePrime!.Value <= parameters.LargePrimeBound);
            AssertRelationValid(r, fb);
        });
    }
}
