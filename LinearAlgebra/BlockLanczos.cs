using System.Collections.Immutable;

namespace LinearAlgebra;

public sealed record BlockLanczosProgress(
    string Stage,
    int Run,
    int Iteration,
    int DimensionsSolved,
    int TargetDimensions,
    TimeSpan Elapsed);

public static class BlockLanczos
{
    public const int DefaultMaxDependencies = 1024;

    private const ulong AllBits = ulong.MaxValue;
    private const long ProgressIntervalMilliseconds = 10_000;
    private const int ParallelVectorDotThreshold = 8192;

    public static SolveResult Solve(
        IReadOnlyList<RelationRow> rows,
        int columnCount,
        int maxDependencies = DefaultMaxDependencies,
        BlockLanczosOptions? options = null,
        IProgress<BlockLanczosProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (maxDependencies <= 0)
        {
            return new SolveResult(Array.Empty<Dependency>(), 0, Solver: "block-lanczos");
        }

        // Already-zero rows are true dependencies. Emit them first because they require no matrix
        // solve, then spend any remaining dependency budget on the non-zero submatrix.
        var nonZeroRows = new List<RelationRow>(rows.Count);
        var originalRowIds = new List<int>(rows.Count);
        var zeroRowIds = new List<int>();
        for (var rowId = 0; rowId < rows.Count; rowId++)
        {
            if (rows[rowId].Count == 0)
            {
                zeroRowIds.Add(rowId);
            }
            else
            {
                originalRowIds.Add(rowId);
                nonZeroRows.Add(rows[rowId]);
            }
        }

        if (nonZeroRows.Count == 0)
        {
            var zeroRowsOnly = zeroRowIds
                .Take(maxDependencies)
                .Select(id => new Dependency(ImmutableArray.Create(id)))
                .ToList();
            return new SolveResult(zeroRowsOnly, 0, Solver: "block-lanczos");
        }

        var dependencies = zeroRowIds
            .Take(maxDependencies)
            .Select(id => new Dependency(ImmutableArray.Create(id)))
            .ToList();
        var remainingDependencyBudget = maxDependencies - dependencies.Count;
        if (remainingDependencyBudget == 0)
        {
            return new SolveResult(dependencies, 0, Solver: "block-lanczos");
        }

        options ??= new BlockLanczosOptions();
        var matrix = BlockLanczosSparseMatrix.FromRelationRows(nonZeroRows, columnCount, options);
        if (matrix.RelationCount == 0)
        {
            return new SolveResult(dependencies, 0, Solver: "block-lanczos");
        }

        var seen = new HashSet<Dependency>(dependencies);
        var lanczosDependencyCount = 0;
        var runs = 0;
        var lastDimensionsSolved = 0;
        var runMilliseconds = new List<long>(4);
        var runDimensions = new List<int>(4);
        for (var retry = 0; retry < 4 && lanczosDependencyCount < remainingDependencyBudget; retry++)
        {
            runs++;
            var runWatch = System.Diagnostics.Stopwatch.StartNew();
            progress?.Report(new BlockLanczosProgress(
                "run-start",
                runs,
                0,
                0,
                matrix.SparseParityColumnCount,
                TimeSpan.Zero));
            if (TrySolveOnce(matrix, retry, runWatch, progress, out var candidateBlock, out var dimensionsSolved))
            {
                runWatch.Stop();
                runMilliseconds.Add(runWatch.ElapsedMilliseconds);
                runDimensions.Add(dimensionsSolved);
                lastDimensionsSolved = Math.Max(lastDimensionsSolved, dimensionsSolved);
                progress?.Report(new BlockLanczosProgress(
                    "extracting",
                    runs,
                    0,
                    dimensionsSolved,
                    matrix.SparseParityColumnCount,
                    runWatch.Elapsed));
                var extracted = BlockLanczosDependencyExtractor.ExtractVerified(
                    nonZeroRows, columnCount, candidateBlock, remainingDependencyBudget - lanczosDependencyCount);
                var acceptedBeforeExtraction = dependencies.Count;
                foreach (var dependency in extracted)
                {
                    if (!matrix.DenseRowsAreZero(dependency.RowIds))
                    {
                        continue;
                    }

                    var mapped = dependency.MapThrough(originalRowIds);
                    if (seen.Add(mapped))
                    {
                        dependencies.Add(mapped);
                        lanczosDependencyCount++;
                        if (lanczosDependencyCount >= remainingDependencyBudget)
                        {
                            break;
                        }
                    }
                }

                progress?.Report(new BlockLanczosProgress(
                    dependencies.Count > acceptedBeforeExtraction ? "dependencies-found" : "no-dependencies",
                    runs,
                    dependencies.Count - acceptedBeforeExtraction,
                    dimensionsSolved,
                    matrix.SparseParityColumnCount,
                    runWatch.Elapsed));

                if (dependencies.Count > acceptedBeforeExtraction)
                {
                    progress?.Report(new BlockLanczosProgress(
                        "run-complete",
                        runs,
                        0,
                        dimensionsSolved,
                        matrix.SparseParityColumnCount,
                        runWatch.Elapsed));
                    return new SolveResult(
                        dependencies,
                        dimensionsSolved,
                        Solver: "block-lanczos",
                        LanczosRuns: runs,
                        LanczosDependencies: lanczosDependencyCount,
                        LanczosRunMilliseconds: runMilliseconds.ToArray(),
                        LanczosRunDimensions: runDimensions.ToArray());
                }
            }
            else
            {
                runWatch.Stop();
                runMilliseconds.Add(runWatch.ElapsedMilliseconds);
                runDimensions.Add(0);
                progress?.Report(new BlockLanczosProgress(
                    "run-failed",
                    runs,
                    0,
                    0,
                    matrix.SparseParityColumnCount,
                    runWatch.Elapsed));
            }
        }

        return new SolveResult(
            dependencies,
            lastDimensionsSolved,
            Solver: "block-lanczos",
            LanczosRuns: runs,
            LanczosDependencies: lanczosDependencyCount,
            LanczosRunMilliseconds: runMilliseconds.ToArray(),
            LanczosRunDimensions: runDimensions.ToArray());
    }

    private static bool TrySolveOnce(
        BlockLanczosSparseMatrix matrix,
        int retry,
        System.Diagnostics.Stopwatch runWatch,
        IProgress<BlockLanczosProgress>? progress,
        out ulong[] candidateBlock,
        out int dimensionsSolved)
    {
        var n = matrix.RelationCount;
        var x = new ulong[n];
        var v0 = new ulong[n];
        var v = new[] { new ulong[n], new ulong[n], new ulong[n] };
        var vnext = new ulong[n];
        var multiplyScratch = new ulong[matrix.SparseParityColumnCount];
        var winv = new[] { new ulong[64], new ulong[64], new ulong[64] };
        var vtAv = new[] { new ulong[64], new ulong[64] };
        var vtA2v = new[] { new ulong[64], new ulong[64] };
        var selected = new[] { new int[64], new int[64] };
        var vectorDotWorkspace = new VectorDotWorkspace(n, matrix.EffectiveParallelism);
        var dim1 = 64;
        var mask1 = AllBits;

        FillInitialVector(x, retry);
        Array.Copy(x, v[0], n);
        matrix.MultiplySymmetric(v[0], v[0], multiplyScratch);
        Array.Copy(v[0], v0, n);
        for (var i = 0; i < 64; i++)
        {
            selected[1][i] = i;
        }

        dimensionsSolved = 0;
        var dim0 = 0;
        var lastProgressMilliseconds = 0L;
        var run = retry + 1;
        for (var iter = 1; iter <= Math.Max(32, n + 128); iter++)
        {
            Array.Clear(vnext);
            matrix.MultiplySymmetric(v[0], vnext, multiplyScratch);
            vtAv[0] = TransposeMultiply(v[0], vnext, vectorDotWorkspace);
            vtA2v[0] = TransposeMultiply(vnext, vnext, vectorDotWorkspace);

            if (Gf2Matrix64.IsZero(vtAv[0]))
            {
                break;
            }

            dim0 = Gf2Matrix64.FindNonsingularSubmatrix(vtAv[0], selected[1], dim1, selected[0], winv[0]);
            if (dim0 == 0)
            {
                break;
            }

            var mask0 = MaskFromSelected(selected[0], dim0);
            if (dimensionsSolved < matrix.SparseParityColumnCount - 64 && (mask0 | mask1) != AllBits)
            {
                dim0 = 0;
                break;
            }

            dimensionsSolved += dim0;
            var elapsedMilliseconds = runWatch.ElapsedMilliseconds;
            if (progress is not null &&
                elapsedMilliseconds - lastProgressMilliseconds >= ProgressIntervalMilliseconds)
            {
                lastProgressMilliseconds = elapsedMilliseconds;
                progress.Report(new BlockLanczosProgress(
                    "iteration",
                    run,
                    iter,
                    dimensionsSolved,
                    matrix.SparseParityColumnCount,
                    runWatch.Elapsed));
            }

            if (mask0 != AllBits)
            {
                for (var i = 0; i < n; i++)
                {
                    vnext[i] &= mask0;
                }
            }

            // v[0]^T * v0, computed exactly every iteration (as msieve does). A d/e/f recurrence is
            // equivalent while the block chain stays B-orthogonal, but the explicit product costs
            // only O(n) per iteration and stays correct independent of that invariant.
            var vtV0 = TransposeMultiply(v[0], v0, vectorDotWorkspace);

            var d = new ulong[64];
            for (var i = 0; i < 64; i++)
            {
                d[i] = (vtA2v[0][i] & mask0) ^ vtAv[0][i];
            }

            d = Gf2Matrix64.Multiply(winv[0], d);
            for (var i = 0; i < 64; i++)
            {
                d[i] ^= 1UL << i;
            }

            Gf2Matrix64.ApplyToBlockVector(v[0], d, vnext);

            var e = Gf2Matrix64.Multiply(winv[1], vtAv[0]);
            for (var i = 0; i < 64; i++)
            {
                e[i] &= mask0;
            }

            Gf2Matrix64.ApplyToBlockVector(v[1], e, vnext);

            if (mask1 != AllBits)
            {
                var f = Gf2Matrix64.Multiply(vtAv[1], winv[1]);
                for (var i = 0; i < 64; i++)
                {
                    f[i] ^= 1UL << i;
                }

                f = Gf2Matrix64.Multiply(winv[2], f);
                var f2 = new ulong[64];
                for (var i = 0; i < 64; i++)
                {
                    f2[i] = ((vtA2v[1][i] & mask1) ^ vtAv[1][i]) & mask0;
                }

                f = Gf2Matrix64.Multiply(f, f2);
                Gf2Matrix64.ApplyToBlockVector(v[2], f, vnext);
            }

            var update = Gf2Matrix64.Multiply(winv[0], vtV0);
            Gf2Matrix64.ApplyToBlockVector(v[0], update, x);

            vnext = Rotate(v, vnext);
            Rotate(winv);
            (vtAv[0], vtAv[1]) = (vtAv[1], vtAv[0]);
            (vtA2v[0], vtA2v[1]) = (vtA2v[1], vtA2v[0]);
            Array.Copy(selected[0], selected[1], 64);
            mask1 = mask0;
            dim1 = dim0;
        }

        if (dim0 == 0)
        {
            candidateBlock = Array.Empty<ulong>();
            return false;
        }

        var sparseProductLength = matrix.SparseParityColumnCount;
        var denseProductLength = matrix.DenseParityColumns.Count;
        var fullProductLength = sparseProductLength + denseProductLength;
        var ax = new ulong[fullProductLength];
        var av = new ulong[fullProductLength];
        matrix.MultiplyA(x, ax, 0, sparseProductLength);
        matrix.MultiplyA(v[0], av, 0, sparseProductLength);
        matrix.MultiplyDenseRows(x, ax, sparseProductLength, denseProductLength);
        matrix.MultiplyDenseRows(v[0], av, sparseProductLength, denseProductLength);
        candidateBlock = BlockLanczosCandidateCombiner.Combine(matrix.RelationCount, fullProductLength, x, v[0], ax, av);
        return true;
    }

    private static void FillInitialVector(ulong[] x, int retry)
    {
        var state = 0x9E3779B97F4A7C15UL ^ ((ulong)retry * 0xD1B54A32D192ED03UL) ^ (ulong)x.Length;
        for (var i = 0; i < x.Length; i++)
        {
            x[i] = SplitMix64(ref state);
        }
    }

    private static ulong SplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong MaskFromSelected(int[] selected, int count)
    {
        ulong mask = 0;
        for (var i = 0; i < count; i++)
        {
            mask |= 1UL << selected[i];
        }

        return mask;
    }

    private static ulong[] TransposeMultiply(ulong[] left, ulong[] right, VectorDotWorkspace workspace)
    {
        if (!workspace.UseParallel || left.Length < ParallelVectorDotThreshold)
        {
            return Gf2Matrix64.TransposeMultiply(left, right);
        }

        if (left.Length != right.Length)
        {
            throw new ArgumentException("Input vectors must have the same length.", nameof(right));
        }

        Parallel.For(0, workspace.Partitions.Length, workspace.ParallelOptions, partitionIndex =>
        {
            var partial = workspace.Partials[partitionIndex];
            Array.Clear(partial);
            var range = workspace.Partitions[partitionIndex];
            var length = range.End - range.Start;
            Gf2Matrix64.TransposeMultiplyAccumulate(
                left.AsSpan(range.Start, length),
                right.AsSpan(range.Start, length),
                partial);
        });

        var result = new ulong[64];
        foreach (var partial in workspace.Partials)
        {
            for (var row = 0; row < result.Length; row++)
            {
                result[row] ^= partial[row];
            }
        }

        return result;
    }

    private sealed class VectorDotWorkspace
    {
        public VectorDotWorkspace(int length, int requestedParallelism)
        {
            var partitionCount = length < ParallelVectorDotThreshold
                ? 1
                : Math.Min(Math.Max(1, requestedParallelism), length);
            Partitions = PartitionRanges.Even(length, partitionCount);
            Partials = new ulong[Partitions.Length][];
            for (var i = 0; i < Partials.Length; i++)
            {
                Partials[i] = new ulong[64];
            }

            ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Partitions.Length };
        }

        public PartitionRange[] Partitions { get; }

        public ulong[][] Partials { get; }

        public ParallelOptions ParallelOptions { get; }

        public bool UseParallel => Partitions.Length > 1;
    }

    private static ulong[] Rotate(ulong[][] values, ulong[] next)
    {
        var old = values[2];
        values[2] = values[1];
        values[1] = values[0];
        values[0] = next;
        Array.Clear(old);
        return old;
    }

    private static void Rotate(ulong[][] values)
    {
        var old = values[2];
        values[2] = values[1];
        values[1] = values[0];
        values[0] = old;
        Array.Clear(values[0]);
    }
}
