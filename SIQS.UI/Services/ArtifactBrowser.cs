namespace SIQS.UI.Services;

/// <summary>Metadata about an artifact file in a job workspace.</summary>
public sealed record ArtifactInfo(string Name, bool Exists, long SizeBytes, DateTimeOffset? LastWriteUtc);

/// <summary>A size-limited text preview of an artifact.</summary>
public sealed record ArtifactPreview(string Name, bool Exists, long SizeBytes, int LineCount, string Text, bool Truncated);

/// <summary>
/// Lists and previews artifacts within a job workspace, enforcing that resolved paths stay inside
/// the configured runs directory and limiting preview size for large files.
/// </summary>
public sealed class ArtifactBrowser
{
    private const int PreviewByteLimit = 64 * 1024;
    private readonly JobWorkspaceResolver _resolver;

    public ArtifactBrowser(JobWorkspaceResolver resolver)
    {
        _resolver = resolver;
    }

    public static readonly IReadOnlyList<string> KnownArtifacts = new[]
    {
        "factor_base.txt", "relations_0000.txt", "partials_0000.txt", "matrix_meta.txt",
        "filtered_matrix.txt", "relations_filtered.txt", "dependencies.txt", "factors.txt",
        "job.json", "events.log",
    };

    public IReadOnlyList<ArtifactInfo> List(string jobId)
    {
        var workspace = _resolver.ResolveJob(jobId);
        var results = new List<ArtifactInfo>();
        foreach (var name in KnownArtifacts)
        {
            var path = _resolver.ResolveArtifact(workspace, name);
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                results.Add(new ArtifactInfo(name, true, info.Length, info.LastWriteTimeUtc));
            }
            else
            {
                results.Add(new ArtifactInfo(name, false, 0, null));
            }
        }

        // Also include any extra relations_*/partials_* batches beyond the first.
        foreach (var path in Directory.EnumerateFiles(workspace.Path, "relations_*.txt")
                     .Concat(Directory.EnumerateFiles(workspace.Path, "partials_*.txt"))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (!KnownArtifacts.Contains(name) && results.All(r => r.Name != name))
            {
                var info = new FileInfo(_resolver.ResolveArtifact(workspace, name));
                results.Add(new ArtifactInfo(name, true, info.Length, info.LastWriteTimeUtc));
            }
        }

        return results;
    }

    public ArtifactPreview Preview(string jobId, string name)
    {
        var workspace = _resolver.ResolveJob(jobId);
        var path = _resolver.ResolveArtifact(workspace, name);
        if (!File.Exists(path))
        {
            return new ArtifactPreview(name, false, 0, 0, string.Empty, false);
        }

        var info = new FileInfo(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        var buffer = new byte[Math.Min(PreviewByteLimit, info.Length)];
        var read = stream.Read(buffer, 0, buffer.Length);
        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        var truncated = info.Length > read;
        var lineCount = text.Count(c => c == '\n') + 1;
        return new ArtifactPreview(name, true, info.Length, lineCount, text, truncated);
    }
}
