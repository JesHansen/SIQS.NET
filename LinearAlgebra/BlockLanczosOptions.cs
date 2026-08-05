namespace LinearAlgebra;

/// <summary>Options for the Block Lanczos linear algebra solver.</summary>
public sealed record BlockLanczosOptions
{
    public const int DefaultPostLanczosRows = 48;
    public const int DefaultMinPostLanczosDimension = 10_000;
    public const int DefaultParallelism = 0;

    /// <summary>
    /// The starting-vector seed used when a matrix is deterministically reproducible by default.
    /// Every retry still derives its own state from this seed (mixed with the retry index), so
    /// changing it gives a run stuck across all retries a genuinely different set of starting
    /// vectors to try, without giving up reproducibility as the default behavior.
    /// </summary>
    public const ulong DefaultSeed = 0x9E3779B97F4A7C15UL;

    public BlockLanczosOptions()
        : this(DefaultPostLanczosRows, DefaultMinPostLanczosDimension, DefaultParallelism, DefaultSeed)
    {
    }

    public BlockLanczosOptions(
        int PostLanczosRows,
        int MinPostLanczosDimension,
        int Parallelism = DefaultParallelism,
        ulong Seed = DefaultSeed)
    {
        if (PostLanczosRows < 0 || PostLanczosRows >= 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PostLanczosRows), PostLanczosRows, "Post-Lanczos row count must be in the range [0, 64).");
        }

        if (MinPostLanczosDimension < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinPostLanczosDimension), MinPostLanczosDimension, "Minimum post-Lanczos dimension must be non-negative.");
        }

        if (Parallelism < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Parallelism), Parallelism, "Parallelism must be zero or positive.");
        }

        this.PostLanczosRows = PostLanczosRows;
        this.MinPostLanczosDimension = MinPostLanczosDimension;
        this.Parallelism = Parallelism;
        this.Seed = Seed;
    }

    public int PostLanczosRows { get; }

    public int MinPostLanczosDimension { get; }

    /// <summary>Seed for the deterministic starting vectors; see <see cref="DefaultSeed"/>.</summary>
    public ulong Seed { get; }

    /// <summary>Requested linear algebra worker count; 0 means <see cref="Environment.ProcessorCount"/>.</summary>
    public int Parallelism { get; }

    public int EffectiveParallelism => Math.Max(1, Parallelism == 0 ? Environment.ProcessorCount : Parallelism);
}
