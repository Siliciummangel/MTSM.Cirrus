using Microsoft.Extensions.Options;

namespace MTSM.Cirrus.Worker;

public sealed class Worker(
    IntegrityCheckProcessor processor,
    IOptions<IntegrityCheckOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly IntegrityCheckOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        string workerInstanceId = ResolveWorkerInstanceId();

        logger.LogInformation(
            "Archive worker {WorkerInstanceId} started. " +
            "Integrity checks enabled: {Enabled}.",
            workerInstanceId,
            _options.Enabled);

        if (!_options.Enabled)
        {
            await WaitForShutdownAsync(cancellationToken);

            logger.LogInformation(
                "Archive worker {WorkerInstanceId} stopped.",
                workerInstanceId);

            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int claimed = await processor.ProcessBatchAsync(
                    workerInstanceId,
                    cancellationToken);

                if (claimed < _options.BatchSize)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(
                            _options.PollingIntervalSeconds),
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }

        logger.LogInformation(
            "Archive worker {WorkerInstanceId} stopped.",
            workerInstanceId);
    }

    private string ResolveWorkerInstanceId()
    {
        string configuredId =
            _options.WorkerInstanceId?.Trim()
            ?? string.Empty;

        string baseId = string.IsNullOrWhiteSpace(configuredId)
            ? Environment.GetEnvironmentVariable("HOSTNAME")
                ?? Environment.MachineName
            : configuredId;

        return $"{baseId}-{Guid.NewGuid():N}";
    }

    private static async Task WaitForShutdownAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
    }
}
