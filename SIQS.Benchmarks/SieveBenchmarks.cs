using System.Numerics;
using BenchmarkDotNet.Attributes;
using Factorbase;
using Sieving;
using SIQS.Contracts.Files;

namespace SIQS.Benchmarks;

/// <summary>
/// Sieve fill/scan/trial-division benchmark through the public in-memory seam
/// (<see cref="SievingEngine.Sieve(FactorBaseDocument, SievingParameters, System.IProgress{SIQS.Contracts.SiqsProgressEvent}?, System.Threading.CancellationToken)"/>),
/// which performs no file I/O. Runs single-threaded and stops after a fixed raw-relation trial target
/// so the measured work is deterministic and bounded. Also isolates <see cref="FactorBaseData.From"/>.
///
/// Fixture: a fixed 40-digit semiprime, so the factor base and polynomial family are identical every
/// run. Kernel refactors must keep relation output identical and throughput within tolerance.
/// </summary>
[MemoryDiagnoser]
public class SieveBenchmarks
{
    // Fixed 40-digit semiprime (deterministic fixture; not sensitive).
    private static readonly BigInteger TargetN =
        BigInteger.Parse("1334471938438661103543925389295924469881");

    private FactorBaseDocument _factorBase = null!;
    private SievingParameters _parameters = null!;

    [GlobalSetup]
    public void Setup()
    {
        var generation = FactorBaseGenerator.Generate(new FactorBaseOptions(TargetN));
        _factorBase = generation.FactorBase
            ?? throw new InvalidOperationException("Benchmark fixture factored during precheck; choose a harder N.");

        // Single-threaded and bounded by a raw-relation trial target so each invocation is
        // deterministic and short enough for BenchmarkDotNet's iteration model.
        _parameters = SievingParameters.Default(_factorBase) with
        {
            Parallelism = 1,
            TrialRawRelationTarget = 2_000,
        };
    }

    [Benchmark]
    public FactorBaseData FactorBaseData_From() => FactorBaseData.From(_factorBase);

    [Benchmark]
    public long Sieve_FillScanTrialDivision()
    {
        var result = SievingEngine.Sieve(_factorBase, _parameters);
        return result.Counters.RawRelations;
    }
}
