using SIQS.UI.Services;

namespace SIQS.UI.Tests;

public sealed class JobWorkspaceResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"siqs-resolver-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("J20260818-123456-0001")]
    [InlineData("D20260818-123456-9999")]
    public void Resolves_generated_job_ids(string jobId)
    {
        var resolver = CreateResolver();

        var workspace = resolver.ResolveJob(jobId);

        Assert.Equal(jobId, workspace.JobId);
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), jobId), workspace.Path);
    }

    [Theory]
    [InlineData("J20260818-123456-0001/../escape")]
    [InlineData("J20260818-123456-0001\\..\\escape")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("C:\\escape")]
    [InlineData("c:\\ESCAPE")]
    [InlineData("j20260818-123456-0001")]
    [InlineData("J20260818-123456-000A")]
    [InlineData("J202608181234560001")]
    public void Rejects_non_generated_job_ids(string jobId)
    {
        var resolver = CreateResolver();

        Assert.Throws<ArgumentException>(() => resolver.ResolveJob(jobId));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("/secret.txt")]
    [InlineData("C:\\secret.txt")]
    public void Rejects_artifacts_outside_workspace(string name)
    {
        var resolver = CreateResolver();
        var workspace = resolver.ResolveJob("J20260818-123456-0001");

        Assert.ThrowsAny<Exception>(() => resolver.ResolveArtifact(workspace, name));
    }

    [Fact]
    public void Resolves_artifact_beneath_validated_workspace()
    {
        var resolver = CreateResolver();
        var workspace = resolver.ResolveJob("J20260818-123456-0001");

        var path = resolver.ResolveArtifact(workspace, "relations_0001.txt");

        Assert.Equal(Path.Combine(workspace.Path, "relations_0001.txt"), path);
    }

    [Fact]
    public void Rejects_existing_job_directory_reparse_point_when_supported()
    {
        var resolver = CreateResolver();
        var outside = Path.Combine(Path.GetTempPath(), $"siqs-resolver-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, "J20260818-123456-0001");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                or PlatformNotSupportedException)
            {
                return;
            }

            Assert.Throws<UnauthorizedAccessException>(() =>
                resolver.ResolveJob("J20260818-123456-0001"));
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            Directory.Delete(outside, recursive: true);
        }
    }

    private JobWorkspaceResolver CreateResolver() => new(new RunsDirectory(_root));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
