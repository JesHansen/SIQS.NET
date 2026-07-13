using SIQS.Contracts;

namespace SIQS.Pipeline;

/// <summary>Owns run-directory creation and phase artifact cleanup policy.</summary>
internal sealed class JobWorkspace
{
    public JobWorkspace(string directory, string jobId)
    {
        DirectoryPath = directory;
        JobId = jobId;
    }

    public string DirectoryPath { get; }
    public string JobId { get; }

    public void EnsureNew()
    {
        if (Directory.Exists(DirectoryPath) && Directory.EnumerateFileSystemEntries(DirectoryPath).Any())
        {
            throw new InvalidOperationException($"Job workspace '{DirectoryPath}' already exists and is not empty.");
        }

        Directory.CreateDirectory(DirectoryPath);
    }

    public void DeletePhaseArtifacts(int startIndex, bool keepSievingArtifacts, IProgress<SiqsProgressEvent>? progress)
    {
        for (var i = startIndex; i < PhaseSequence.All.Length; i++)
        {
            var phase = PhaseSequence.All[i];
            if (phase == SiqsPhase.Sieving && keepSievingArtifacts)
            {
                continue;
            }

            foreach (var path in ArtifactPathsForPhase(phase))
            {
                File.Delete(path);
                progress?.Report(new SiqsProgressEvent(
                    DateTimeOffset.UtcNow, JobId, SiqsPhase.Pipeline, ProgressLevel.Info,
                    "deleted stale artifact", null, new Dictionary<string, string>(), Path.GetFileName(path)));
            }
        }
    }

    public IEnumerable<string> ArtifactPathsForPhase(SiqsPhase phase)
    {
        string[] exact = phase switch
        {
            SiqsPhase.FactorBase => ["factor_base.txt"],
            SiqsPhase.Filtering => ["relations_filtered.txt", "filtered_matrix.txt", "matrix_meta.txt"],
            SiqsPhase.LinearAlgebra => ["dependencies.txt"],
            SiqsPhase.SquareRoot => ["factors.txt"],
            _ => [],
        };

        foreach (var name in exact)
        {
            var path = Path.Combine(DirectoryPath, name);
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        if (phase == SiqsPhase.Sieving)
        {
            foreach (var path in RawBatchFiles.Enumerate(DirectoryPath))
            {
                yield return path;
            }

            var checkpoint = Path.Combine(DirectoryPath, "sieve_checkpoint.txt");
            if (File.Exists(checkpoint))
            {
                yield return checkpoint;
            }
        }
    }
}
