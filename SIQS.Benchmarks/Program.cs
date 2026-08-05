using BenchmarkDotNet.Running;
using SIQS.Benchmarks;

if (args is ["--compare-spv", ..])
{
    return SmallPrimeVariationComparisonTool.Run(args[1..]);
}

if (args is ["--capture-residuals", ..])
{
    return ResidualCorpusTool.Run(args[1..]);
}

if (args is ["--benchmark-residuals", ..])
{
    return ResidualCorpusBenchmarkTool.Run(args[1..]);
}

if (args is ["--replay-cofactor", ..])
{
    return ReplayCofactorTool.Run(args[1..]);
}

if (args is ["--screen-share", ..])
{
    return ScreenShareTool.Run(args[1..]);
}

if (args is ["--candidate-scaling", ..])
{
    return CandidateScalingTool.Run(args[1..]);
}

// Runs every *Benchmarks class in this assembly. Filter from the command line, e.g.
//   dotnet run -c Release --project SIQS.Benchmarks -- --filter *BlockLanczosMatrixBenchmarks*
BenchmarkSwitcher.FromAssembly(typeof(BlockLanczosMatrixBenchmarks).Assembly).Run(args);
return 0;
