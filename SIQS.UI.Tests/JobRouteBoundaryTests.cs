using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SIQS.Contracts;
using SIQS.Pipeline;
using SIQS.UI.Services;

namespace SIQS.UI.Tests;

public sealed class JobRouteBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"siqs-http-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("..%5C..%5Cescaped")]
    [InlineData("..%2F..%2Fescaped")]
    [InlineData("%2E%2E")]
    [InlineData("C:%5Cescaped")]
    [InlineData("j20260818-123456-0001")]
    public async Task Invalid_route_job_ids_return_not_found_without_reading_outside_runs(string routeJobId)
    {
        var escaped = Path.Combine(_root, "escaped");
        Directory.CreateDirectory(escaped);
        File.WriteAllText(Path.Combine(escaped, "job.json"), "outside-runs-marker");
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync($"/jobs/{routeJobId}/artifacts/job.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("outside-runs-marker", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Url_decoding_preserves_valid_job_routes_and_normal_artifacts()
    {
        const string jobId = "J20260818-123456-0001";
        var runs = Path.Combine(_root, "runs");
        var directory = Path.Combine(runs, jobId);
        Directory.CreateDirectory(directory);
        JobStore.Write(directory, new JobState
        {
            JobId = jobId,
            TargetN = "10403",
            Status = JobStatus.CompletedFactorFound,
            FinalFactors = ["101", "103"],
        });
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        var page = await client.GetAsync("/jobs/%4A20260818-123456-0001");
        var artifact = await client.GetAsync("/jobs/%4A20260818-123456-0001/artifacts/job.json");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(jobId, await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, artifact.StatusCode);
        Assert.Contains("10403", await artifact.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var runs = Path.Combine(_root, "runs");
        Directory.CreateDirectory(runs);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<RunsDirectory>();
                services.RemoveAll<JobWorkspaceResolver>();
                services.AddSingleton(new RunsDirectory(runs));
                services.AddSingleton<JobWorkspaceResolver>();
            });
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
