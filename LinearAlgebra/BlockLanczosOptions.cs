namespace LinearAlgebra;

/// <summary>Options for the Block Lanczos linear algebra solver.</summary>
public sealed record BlockLanczosOptions
{
    public const int DefaultPostLanczosRows = 48;
    public const int DefaultMinPostLanczosDimension = 10_000;
    public const int DefaultParallelism = 0;

    public BlockLanczosOptions()
        : this(DefaultPostLanczosRows, DefaultMinPostLanczosDimension, DefaultParallelism)
    {
    }

    public BlockLanczosOptions(
        int PostLanczosRows,
        int MinPostLanczosDimension,
        int Parallelism = DefaultParallelism)
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
    }

    public int PostLanczosRows { get; }

    public int MinPostLanczosDimension { get; }

    /// <summary>Requested linear algebra worker count; 0 means <see cref="Environment.ProcessorCount"/>.</summary>
    public int Parallelism { get; }

    public int EffectiveParallelism => Math.Max(1, Parallelism == 0 ? Environment.ProcessorCount : Parallelism);
}
