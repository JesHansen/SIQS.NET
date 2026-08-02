using System.Numerics;
using Microsoft.AspNetCore.Mvc;
using SIQS.Contracts.Distributed;
using SIQS.Overlord;
using SIQS.Pipeline;

namespace SIQS.UI;

/// <summary>Admin request to start a distributed factorization. Optional fields fall back to pipeline defaults.</summary>
internal sealed record DistSubmitRequest(
    string N,
    long? FactorBaseBound = null,
    string? Multiplier = null,
    long? HalfInterval = null,
    int? APrimeCount = null,
    int? APrimeWindowSize = null,
    int? ErrorMargin = null,
    int? RelationTarget = null,
    long? PolynomialCount = null);

/// <summary>
/// The REST surface volunteer clients talk to: a version handshake, a job descriptor poll, work
/// leasing, and durable relation upload. Each endpoint is a thin adapter over <see cref="OverlordService"/>.
/// </summary>
internal static class DistributedEndpoints
{
    public static IEndpointRouteBuilder MapDistributedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dist").DisableAntiforgery();

        group.MapPost("/hello", (HelloRequest request, OverlordService overlord)
            => Results.Ok(overlord.Hello(request)));

        group.MapGet("/job", (OverlordService overlord)
            => overlord.TryGetJob() is { } descriptor ? Results.Ok(descriptor) : Results.NoContent());

        group.MapPost("/lease", (int? parallelism, OverlordService overlord)
            => overlord.TryLease(parallelism) is { } lease ? Results.Ok(lease) : Results.NoContent());

        group.MapPost("/relations/{jobId}/{leaseId}/{sequence:long}", async (
                string jobId,
                string leaseId,
                long sequence,
                HttpRequest request,
                OverlordService overlord,
                CancellationToken cancellationToken)
            => Results.Ok(await overlord.UploadChunkAsync(
                jobId, leaseId, sequence, request.Body, cancellationToken)))
            .WithMetadata(new DisableRequestSizeLimitAttribute());

        group.MapPost("/relations/{jobId}/{leaseId}/complete", async (
                string jobId,
                string leaseId,
                LeaseUploadCompleteRequest request,
                OverlordService overlord,
                CancellationToken cancellationToken)
            => Results.Ok(await overlord.CompleteLeaseUploadAsync(
                jobId, leaseId, request.ChunkCount, cancellationToken)));

        group.MapGet("/status", (OverlordService overlord)
            => overlord.Snapshot() is { } snapshot ? Results.Ok(snapshot) : Results.NoContent());

        group.MapGet("/client", (IWebHostEnvironment env) => ClientDownload("windows-x64", env));
        group.MapGet("/client/{platform}", (string platform, IWebHostEnvironment env) => ClientDownload(platform, env));

        group.MapPost("/submit", (DistSubmitRequest body, OverlordService overlord) =>
        {
            FactorizationRequest request;
            try
            {
                request = new FactorizationRequest(BigInteger.Parse(body.N))
                {
                    FactorBase = new FactorBaseRunOptions
                    {
                        Bound = body.FactorBaseBound,
                        Multiplier = body.Multiplier is null ? null : BigInteger.Parse(body.Multiplier),
                    },
                    Sieving = new SievingRunOptions
                    {
                        HalfInterval = body.HalfInterval,
                        APrimeCount = body.APrimeCount,
                        APrimeWindowSize = body.APrimeWindowSize,
                        ErrorMargin = body.ErrorMargin,
                        RelationTarget = body.RelationTarget,
                        PolynomialCount = body.PolynomialCount,
                    },
                };
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return Results.BadRequest($"Invalid request: {ex.Message}");
            }

            try
            {
                overlord.Submit(request);
                return Results.Ok(overlord.Snapshot());
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        });

        return app;
    }

    private static IResult ClientDownload(string platform, IWebHostEnvironment env)
    {
        var artifact = platform switch
        {
            "windows-x64" => new ClientArtifact("windows-x64", "qs-sieve-client.exe"),
            "linux-x64" => new ClientArtifact("linux-x64", "qs-sieve-client"),
            _ => null,
        };

        if (artifact is null)
        {
            return Results.NotFound("Unknown client platform. Available platforms are windows-x64 and linux-x64.");
        }

        var path = Path.Combine(env.ContentRootPath, "download", artifact.Platform, artifact.FileName);
        return File.Exists(path)
            ? Results.File(path, "application/octet-stream", artifact.FileName)
            : Results.NotFound(
                "The client executable has not been published yet. An admin can publish it with: .\\build.ps1");
    }

    private sealed record ClientArtifact(string Platform, string FileName);
}
