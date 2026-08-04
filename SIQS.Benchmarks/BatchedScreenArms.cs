namespace SIQS.Benchmarks;

/// <summary>
/// Registration hook for the Experiment 38 batched/SIMD cofactor-screen replay arms. On branch 36
/// this is a no-op; Experiment 38 replaces it with the batched screen + splitter variants.
/// </summary>
internal static class BatchedScreenArms
{
    public static void Register(Dictionary<string, Func<ulong, ulong>> arms)
    {
        // No batched-screen arms on the corpus/harness branch. See Experiment 38.
    }
}
