using Microsoft.Extensions.Options;
using MTSM.Cirrus.Worker.Maintenance;

namespace MTSM.Cirrus.Worker;

public sealed class Worker(
    StorageProcessingProcessor storageProcessingProcessor,
    StoragePackingProcessor storagePackingProcessor,
    PackMaintenanceProcessor packMaintenanceProcessor,
    IntegrityCheckProcessor integrityProcessor,
    PurgeProcessor purgeProcessor,
    IOptions<StorageProcessingOptions> storageProcessingOptions,
    IOptions<IntegrityCheckOptions> integrityOptions,
    IOptions<PurgeOptions> purgeOptions,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly StorageProcessingOptions _storageProcessingOptions =
        storageProcessingOptions.Value;
    private readonly IntegrityCheckOptions _integrityOptions = integrityOptions.Value;
    private readonly PurgeOptions _purgeOptions = purgeOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        string workerInstanceId = ResolveWorkerInstanceId();

        logger.LogInformation(
            "Archive worker {WorkerInstanceId} started. " +
            "Storage processing enabled: {StorageProcessingEnabled}; " +
            "Integrity checks enabled: {IntegrityEnabled}; " +
            "purge enabled: {PurgeEnabled}.",
            workerInstanceId,
            _storageProcessingOptions.Enabled,
            _integrityOptions.Enabled,
            _purgeOptions.Enabled);

        if (!_storageProcessingOptions.Enabled
            && !_integrityOptions.Enabled
            && !_purgeOptions.Enabled)
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
                int storageProcessingClaimed =
                    _storageProcessingOptions.Enabled
                        ? await storageProcessingProcessor.ProcessBatchAsync(
                            workerInstanceId,
                            cancellationToken)
                        : 0;
                int storagePackingClaimed =
                    _storageProcessingOptions.Enabled
                        ? await storagePackingProcessor.ProcessBatchAsync(
                            workerInstanceId,
                            cancellationToken)
                        : 0;
                int packsMaintained = _storageProcessingOptions.Enabled
                    && _storageProcessingOptions.PackMaintenanceEnabled
                    ? await packMaintenanceProcessor.ProcessBatchAsync(workerInstanceId, cancellationToken)
                    : 0;
                int integrityClaimed = _integrityOptions.Enabled
                    ? await integrityProcessor.ProcessBatchAsync(workerInstanceId, cancellationToken)
                    : 0;
                int purgeClaimed = _purgeOptions.Enabled
                    ? await purgeProcessor.ProcessBatchAsync(workerInstanceId, cancellationToken)
                    : 0;

                if (storageProcessingClaimed
                        < _storageProcessingOptions.BatchSize
                    && storagePackingClaimed
                        < _storageProcessingOptions.BatchSize
                    && packsMaintained < _storageProcessingOptions.PackMaintenanceBatchSize
                    && integrityClaimed < _integrityOptions.BatchSize
                    && purgeClaimed < _purgeOptions.BatchSize)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(
                            Math.Min(
                                _storageProcessingOptions.Enabled
                                    ? _storageProcessingOptions.PollingIntervalSeconds
                                    : int.MaxValue,
                                Math.Min(
                                    _integrityOptions.Enabled
                                        ? _integrityOptions.PollingIntervalSeconds
                                        : int.MaxValue,
                                    _purgeOptions.Enabled
                                        ? _purgeOptions.PollingIntervalSeconds
                                        : int.MaxValue))),
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
