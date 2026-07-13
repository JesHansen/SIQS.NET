using BenchmarkDotNet.Running;
using SIQS.Benchmarks;

// Runs every *Benchmarks class in this assembly. Filter from the command line, e.g.
//   dotnet run -c Release --project SIQS.Benchmarks -- --filter *BlockLanczosMatrixBenchmarks*
BenchmarkSwitcher.FromAssembly(typeof(BlockLanczosMatrixBenchmarks).Assembly).Run(args);
