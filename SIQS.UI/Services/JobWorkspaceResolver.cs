namespace SIQS.UI.Services;

/// <summary>A job directory proven to be a direct child of the configured runs directory.</summary>
public sealed class JobWorkspacePath
{
    internal JobWorkspacePath(string jobId, string path)
    {
        JobId = jobId;
        Path = path;
    }

    public string JobId { get; }
    public string Path { get; }
}

/// <summary>
/// Resolves route-supplied job and artifact identifiers without allowing them to escape the
/// configured runs directory. Existing reparse points are rejected because another process may
/// modify the runs tree in deployments that expose distributed workers.
/// </summary>
public sealed class JobWorkspaceResolver
{
    private readonly string _runsRoot;
    private readonly string _runsRootPrefix;
    private readonly StringComparison _pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public JobWorkspaceResolver(RunsDirectory runsDirectory)
    {
        _runsRoot = Path.GetFullPath(runsDirectory.Path);
        _runsRootPrefix = Path.EndsInDirectorySeparator(_runsRoot)
            ? _runsRoot
            : _runsRoot + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_runsRoot);
    }

    public JobWorkspacePath ResolveJob(string jobId)
    {
        if (!IsGeneratedJobId(jobId))
        {
            throw new ArgumentException("The job id is not a generated SIQS job identifier.", nameof(jobId));
        }

        var candidate = Path.GetFullPath(Path.Combine(_runsRoot, jobId));
        EnsureChild(candidate, _runsRootPrefix, "job directory");
        RejectReparsePoint(candidate, "job directory");
        return new JobWorkspacePath(jobId, candidate);
    }

    public string ResolveArtifact(JobWorkspacePath workspace, string name)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!workspace.Path.StartsWith(_runsRootPrefix, _pathComparison))
        {
            throw new UnauthorizedAccessException("The job workspace does not belong to the configured runs directory.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("The artifact name is required.", nameof(name));
        }

        var workspacePrefix = Path.EndsInDirectorySeparator(workspace.Path)
            ? workspace.Path
            : workspace.Path + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(workspace.Path, name));
        EnsureChild(candidate, workspacePrefix, "artifact");
        RejectReparsePointsBetween(workspace.Path, candidate);
        return candidate;
    }

    public static bool IsGeneratedJobId(string? value)
    {
        if (value is not { Length: 21 } || value[0] is not ('J' or 'D') ||
            value[9] != '-' || value[16] != '-')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (index is 9 or 16)
            {
                continue;
            }

            if (value[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private void EnsureChild(string candidate, string parentPrefix, string description)
    {
        if (!candidate.StartsWith(parentPrefix, _pathComparison))
        {
            throw new UnauthorizedAccessException($"The resolved {description} escapes its configured parent.");
        }
    }

    private static void RejectReparsePointsBetween(string workspace, string candidate)
    {
        RejectReparsePoint(workspace, "job directory");
        var relative = Path.GetRelativePath(workspace, candidate);
        var current = workspace;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            RejectReparsePoint(current, "artifact path");
        }
    }

    private static void RejectReparsePoint(string path, string description)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException($"The resolved {description} contains a reparse point.");
        }
    }
}
