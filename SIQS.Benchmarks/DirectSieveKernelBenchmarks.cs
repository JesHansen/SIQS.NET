using System.Numerics;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Factorbase;
using Sieving;

namespace SIQS.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class DirectSieveKernelBenchmarks
{
    private const int OperationsPerInvoke = 64;
    private static readonly BigInteger Target = BigInteger.Parse(
        "187283238463394422587702976618342993588950735777776598742514519462549381091610809601019");

    private FactorBaseData _factorBase = null!;
    private DirectSievePlan _plan = null!;
    private byte[] _logCredits = null!;
    private int[] _initial1 = null!;
    private int[] _initial2 = null!;
    private int[] _position1 = null!;
    private int[] _position2 = null!;
    private byte[] _sieve = null!;
    private int _blockSize;
    private int _fullLength;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var document = FactorBaseGenerator.Generate(new FactorBaseOptions(Target)).FactorBase!;
        _factorBase = FactorBaseData.From(document);
        var parameters = SievingParameters.Default(document);
        _blockSize = parameters.EffectiveSieveBlockSize;
        _fullLength = checked((int)(2 * parameters.SieveHalfInterval + 1));
        var smallPrimeCount = 0;
        while (smallPrimeCount < _factorBase.Count && _factorBase.Primes[smallPrimeCount] <= 31)
        {
            smallPrimeCount++;
        }

        _plan = DirectSievePlan.Build(_factorBase, smallPrimeCount, parameters);
        _logCredits = _factorBase.LogP.Select(value => (byte)Math.Clamp(value / 16, 1, 255)).ToArray();
        _initial1 = new int[_factorBase.Count];
        _initial2 = new int[_factorBase.Count];
        var m = parameters.SieveHalfInterval;
        for (var i = 0; i < _factorBase.Count; i++)
        {
            var prime = _factorBase.Primes[i];
            _initial1[i] = (int)((_factorBase.Root1[i] + m) % prime);
            var second = (int)((_factorBase.Root2[i] + m) % prime);
            _initial2[i] = second == _initial1[i] ? _fullLength : second;
        }

        _position1 = new int[_factorBase.Count];
        _position2 = new int[_factorBase.Count];
        _sieve = new byte[_blockSize];
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerInvoke)]
    public int Generic()
    {
        var checksum = 0;
        for (var operation = 0; operation < OperationsPerInvoke; operation++)
        {
            ResetState();
            ref var sieve0 = ref MemoryMarshal.GetArrayDataReference(_sieve);
            DirectSieveKernel.FillGeneric(
                ref sieve0, 0, _blockSize, _fullLength,
                _factorBase.Primes, _logCredits, _position1, _position2,
                _plan.StartIndex, _plan.BucketStart);
            checksum ^= _position1[_plan.BucketStart - 1] + _sieve[_blockSize - 1];
        }

        return checksum;
    }

    [Benchmark(OperationsPerInvoke = OperationsPerInvoke)]
    public int Banded()
    {
        var checksum = 0;
        for (var operation = 0; operation < OperationsPerInvoke; operation++)
        {
            ResetState();
            ref var sieve0 = ref MemoryMarshal.GetArrayDataReference(_sieve);
            DirectSieveKernel.FillBanded(
                ref sieve0, 0, _blockSize, _fullLength,
                _plan, _logCredits, _position1, _position2);
            checksum ^= _position1[_plan.BucketStart - 1] + _sieve[_blockSize - 1];
        }

        return checksum;
    }

    private void ResetState()
    {
        _initial1.CopyTo(_position1, 0);
        _initial2.CopyTo(_position2, 0);
        Array.Clear(_sieve);
    }
}
