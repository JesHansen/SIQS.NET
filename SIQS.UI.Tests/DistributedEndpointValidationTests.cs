using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using SIQS.Contracts.Distributed;
using SIQS.Overlord;
using SIQS.Pipeline;
using SIQS.UI.Services;

namespace SIQS.UI.Tests;

public sealed class DistributedEndpointValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "siqs-dist-http-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Invalid_submission_returns_field_errors_without_creating_a_job()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.PostAsJsonAsync("/api/dist/submit", new
        {
            n = "91",
            halfInterval = FactorizationRequestLimits.MaxSieveHalfInterval + 1,
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SieveHalfInterval", body, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Fact]
    public async Task Invalid_active_job_submission_is_400_while_valid_conflict_is_409()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        var valid = new
        {
            n = "1022117",
            factorBaseBound = 1_000,
            multiplier = "1",
            halfInterval = 20_000,
            aPrimeCount = 2,
            aPrimeWindowSize = 24,
            errorMargin = 20,
            relationTarget = 150,
            polynomialCount = 200_000,
        };
        var accepted = await client.PostAsJsonAsync("/api/dist/submit", valid);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var invalid = await client.PostAsJsonAsync("/api/dist/submit", new
        {
            n = "1022117",
            halfInterval = FactorizationRequestLimits.MaxSieveHalfInterval + 1,
        });
        var conflict = await client.PostAsJsonAsync("/api/dist/submit", valid);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Relation_upload_rejects_oversized_content_length_before_dispatch()
    {
        using var factory = CreateFactory(new OverlordOptions
        {
            MaxRelationChunkBytes = 4,
            MaxRelationBacklogBytes = 8,
            MaxRelationInboxBytes = 32,
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        using var content = new ByteArrayContent("12345"u8.ToArray());
        content.Headers.ContentType = new("application/x-ndjson");

        var response = await client.PostAsync(
            "/api/dist/relations/D20260818-000000-0000/lease-one/0", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Relation_upload_requires_ndjson_content_type()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        using var content = new StringContent("{}");

        var response = await client.PostAsync(
            "/api/dist/relations/D20260818-000000-0000/lease-one/0", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Chunked_relation_upload_is_stopped_by_streaming_limit()
    {
        using var factory = CreateFactory(new OverlordOptions
        {
            MaxRelationChunkBytes = 4,
            MaxRelationBacklogBytes = 8,
            MaxRelationInboxBytes = 32,
        });
        var overlord = factory.Services.GetRequiredService<OverlordService>();
        overlord.Submit(new FactorizationRequest(System.Numerics.BigInteger.Parse("1022117"))
        {
            FactorBase = new FactorBaseRunOptions { Bound = 1_000, Multiplier = 1 },
            Sieving = new SievingRunOptions
            {
                HalfInterval = 20_000,
                APrimeCount = 2,
                APrimeWindowSize = 24,
                ErrorMargin = 20,
                RelationTarget = 150,
                PolynomialCount = 200_000,
            },
        });
        while (overlord.Current!.Phase == OverlordPhase.Preparing)
        {
            await Task.Delay(10);
        }

        var lease = Assert.IsType<LeaseResponse>(overlord.TryLease());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        using var content = new ChunkedContent("12345"u8.ToArray());
        content.Headers.ContentType = new("application/x-ndjson");
        Assert.Null(content.Headers.ContentLength);

        var response = await client.PostAsync(
            $"/api/dist/relations/{lease.JobId}/{lease.LeaseId}/0", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Missing_client_endpoint_and_page_show_the_authoritative_publish_instructions()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var endpoint = await client.GetAsync("/api/dist/client/linux-arm64");
        var endpointBody = await endpoint.Content.ReadAsStringAsync();
        var pageBody = await client.GetStringAsync("/distributed");

        Assert.Equal(HttpStatusCode.NotFound, endpoint.StatusCode);
        Assert.Contains("build-ui.ps1", endpointBody, StringComparison.Ordinal);
        Assert.Contains("dotnet publish SIQS.UI/SIQS.UI.csproj", endpointBody, StringComparison.Ordinal);
        Assert.Contains("-Runtimes linux-arm64", endpointBody, StringComparison.Ordinal);
        Assert.Contains("build-ui.ps1", pageBody, StringComparison.Ordinal);
        Assert.Contains("dotnet publish SIQS.UI/SIQS.UI.csproj", pageBody, StringComparison.Ordinal);
        Assert.Contains("-Runtimes linux-arm64,osx-arm64", pageBody, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateFactory(OverlordOptions? options = null)
    {
        Directory.CreateDirectory(_root);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<OverlordService>();
                services.RemoveAll<SieveClientCatalog>();
                services.AddSingleton(_ => new OverlordService(_root, options));
                services.AddSingleton(new SieveClientCatalog(new TestEnvironment(_root)));
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

    private sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class TestEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SIQS.UI.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(contentRoot, "wwwroot");
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
