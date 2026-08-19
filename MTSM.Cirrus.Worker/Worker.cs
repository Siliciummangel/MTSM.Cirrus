using Microsoft.Extensions.Options;

namespace MTSM.Cirrus.Worker;

public sealed class Worker(
    IntegrityCheckProcessor integrityProcessor,
    PurgeProcessor purgeProcessor,
    IOptions<IntegrityCheckOptions> integrityOptions,
    IOptions<PurgeOptions> purgeOptions,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly IntegrityCheckOptions _integrityOptions = integrityOptions.Value;
    private readonly PurgeOptions _purgeOptions = purgeOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        string workerInstanceId = ResolveWorkerInstanceId();

        logger.LogInformation(
            "Archive worker {WorkerInstanceId} started. " +
            "Integrity checks enabled: {IntegrityEnabled}; " +
            "purge enabled: {PurgeEnabled}.",
            workerInstanceId,
            _integrityOptions.Enabled,
            _purgeOptions.Enabled);

        if (!_integrityOptions.Enabled && !_purgeOptions.Enabled)
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
                int integrityClaimed = _integrityOptions.Enabled
                    ? await integrityProcessor.ProcessBatchAsync(workerInstanceId, cancellationToken)
                    : 0;
                int purgeClaimed = _purgeOptions.Enabled
                    ? await purgeProcessor.ProcessBatchAsync(workerInstanceId, cancellationToken)
                    : 0;

                if (integrityClaimed < _integrityOptions.BatchSize
                    && purgeClaimed < _purgeOptions.BatchSize)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(
                            Math.Min(
                                _integrityOptions.Enabled
                                    ? _integrityOptions.PollingIntervalSeconds
                                    : int.MaxValue,
                                _purgeOptions.Enabled
                                    ? _purgeOptions.PollingIntervalSeconds
                                    : int.MaxValue)),
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
            _integrityOptions.WorkerInstanceId?.Trim()
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
