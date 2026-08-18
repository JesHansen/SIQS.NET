using SIQS.UI.Services;

namespace SIQS.UI;

/// <summary>Rejects malformed route job identifiers before Razor components can access the filesystem.</summary>
internal sealed class RunsDirectoryBoundaryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, JobWorkspaceResolver resolver)
    {
        if (!TryGetJobRoute(context.Request.Path, out var jobId, out var artifactName))
        {
            await next(context);
            return;
        }

        try
        {
            var workspace = resolver.ResolveJob(jobId);
            if (artifactName is not null)
            {
                _ = resolver.ResolveArtifact(workspace, artifactName);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException
            or NotSupportedException or PathTooLongException)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    }

    private static bool TryGetJobRoute(PathString path, out string jobId, out string? artifactName)
    {
        jobId = string.Empty;
        artifactName = null;
        var value = path.Value;
        if (value is null || !value.StartsWith("/jobs/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = value[6..];
        var slash = remainder.IndexOf('/');
        var encodedJobId = slash < 0 ? remainder : remainder[..slash];
        jobId = Uri.UnescapeDataString(encodedJobId);
        if (slash < 0)
        {
            return true;
        }

        const string artifactPrefix = "/artifacts/";
        var afterJob = remainder[slash..];
        if (afterJob.StartsWith(artifactPrefix, StringComparison.OrdinalIgnoreCase))
        {
            artifactName = Uri.UnescapeDataString(afterJob[artifactPrefix.Length..]);
        }

        return true;
    }
}
