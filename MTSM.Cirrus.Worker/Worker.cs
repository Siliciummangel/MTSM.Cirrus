namespace MTSM.Cirrus.Worker;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Archive worker started. No background tasks are configured in the MVP.");

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }

        logger.LogInformation(
            "Archive worker stopped.");
    }
}