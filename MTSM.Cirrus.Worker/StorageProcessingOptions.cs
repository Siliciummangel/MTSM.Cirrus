namespace MTSM.Cirrus.Worker;

public sealed class StorageProcessingOptions
{
    public const string SectionName = "StorageProcessing";

    public bool Enabled { get; init; } = true;
    public int PollingIntervalSeconds { get; init; } = 10;
    public int BatchSize { get; init; } = 10;
    public int MaxConcurrency { get; init; } = 2;
    public int LeaseDurationMinutes { get; init; } = 30;
    public int InitialRetryDelaySeconds { get; init; } = 30;
    public int MaximumRetryDelayMinutes { get; init; } = 60;
    public int MaximumAttempts { get; init; } = 10;
}
