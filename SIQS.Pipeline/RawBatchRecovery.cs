using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Repairs an interrupted append at the recoverable tail of canonical relation batches.</summary>
public static class RawBatchRecovery
{
    public static void RepairTails(string jobDirectory, IProgress<SiqsProgressEvent>? progress = null)
    {
        PhaseArtifactStore.QuarantineCorruptTailBatch(jobDirectory, "relations", progress);
        PhaseArtifactStore.QuarantineCorruptTailBatch(jobDirectory, "partials", progress);
    }
}
