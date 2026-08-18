using System.Numerics;
using Microsoft.AspNetCore.Http.Features;
using SIQS.Contracts.Distributed;
using SIQS.Overlord;
using SIQS.Pipeline;
using SIQS.UI.Services;

namespace SIQS.UI;

/// <summary>
/// Request to start a distributed factorization. Optional fields fall back to pipeline defaults.
/// There is no authentication on this endpoint: anyone who can reach the server can submit a job.
/// </summary>
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

        group.MapPost("/relations/{jobId}/{leaseId}/{sequence:long}", async Task<IResult> (
                string jobId,
                string leaseId,
                long sequence,
                HttpRequest request,
                OverlordService overlord,
                CancellationToken cancellationToken) =>
            {
                const long transportMarginBytes = 64 * 1024;
                var transportLimit = overlord.MaxRelationChunkBytes > long.MaxValue - transportMarginBytes
                    ? long.MaxValue
                    : overlord.MaxRelationChunkBytes + transportMarginBytes;
                var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                {
                    sizeFeature.MaxRequestBodySize = transportLimit;
                }

                if (request.ContentLength > overlord.MaxRelationChunkBytes)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status413PayloadTooLarge,
                        detail: $"Relation chunks cannot exceed {overlord.MaxRelationChunkBytes} bytes.");
                }

                var mediaType = request.ContentType?.Split(';', 2)[0].Trim();
                if (!string.Equals(mediaType, "application/x-ndjson", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(mediaType, "application/ndjson", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status415UnsupportedMediaType,
                        detail: "Relation chunks must use application/x-ndjson.");
                }

                var response = await overlord.UploadChunkAsync(
                    jobId, leaseId, sequence, request.Body, cancellationToken).ConfigureAwait(false);
                return Results.Ok(response);
            });

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

        group.MapGet("/recoverable", (OverlordService overlord)
            => Results.Ok(overlord.ListRecoverableJobs()));

        group.MapPost("/recover/{jobId}", (string jobId, OverlordService overlord) =>
        {
            try
            {
                overlord.Recover(jobId);
                return Results.Ok(overlord.Snapshot());
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                return Results.Problem(
                    title: "Distributed recovery state is invalid",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });

        group.MapGet("/client", (SieveClientCatalog clients) => ClientDownload(SieveClientCatalog.Default.Platform, clients));
        group.MapGet("/client/{platform}", (string platform, SieveClientCatalog clients) => ClientDownload(platform, clients));

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
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = [$"Invalid request: {ex.Message}"],
                });
            }

            try
            {
                overlord.Submit(request);
                return Results.Ok(overlord.Snapshot());
            }
            catch (FactorizationRequestValidationException ex)
            {
                return Results.ValidationProblem(ex.Issues
                    .GroupBy(issue => issue.Field, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(issue => issue.Message).ToArray(),
                        StringComparer.Ordinal));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        });

        return app;
    }

    private static IResult ClientDownload(string platform, SieveClientCatalog clients)
    {
        if (SieveClientCatalog.Find(platform) is not { } client)
        {
            return Results.NotFound(
                $"Unknown client platform '{platform}'. Known platforms are "
                + $"{string.Join(", ", SieveClientCatalog.Known.Select(known => known.Platform))}.");
        }

        if (clients.IsPublished(client))
        {
            return Results.File(clients.PathTo(client), "application/octet-stream", client.FileName);
        }

        // The clients are build output, not source, so a server started with `dotnet run` has none.
        // Say so, rather than returning a bare 404 that reads like a broken deployment.
        var available = clients.Published;
        return Results.NotFound(
            $"No {client.DisplayName} client has been published on this server. "
            + "The sieve clients are build output: run `./build-ui.ps1` (or `dotnet publish "
            + $"SIQS.UI/SIQS.UI.csproj -c Release`) to produce them, adding `-Runtimes {client.RuntimeIdentifier}` "
            + "for a platform outside the default set. "
            + (available.Count > 0
                ? $"Currently available here: {string.Join(", ", available.Select(published => published.Platform))}."
                : "This server currently has no published clients at all."));
    }
}
