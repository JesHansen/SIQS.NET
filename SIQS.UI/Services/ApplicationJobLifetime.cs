using SIQS.Overlord;

namespace SIQS.UI.Services;

/// <summary>Joins both job systems during host shutdown under the configured host timeout.</summary>
public sealed class ApplicationJobLifetime(
    FactorizationJobService localJobs,
    OverlordService distributedJobs,
    ILogger<ApplicationJobLifetime> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(
                localJobs.StopAsync(cancellationToken),
                distributedJobs.StopAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                "SIQS host shutdown timed out. Local job {LocalJobId} and distributed job {DistributedJobId} remain resumable from their last atomic state.",
                localJobs.Current?.JobId,
                distributedJobs.Current?.JobId);
        }
    }
}
