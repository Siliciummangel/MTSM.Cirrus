namespace MTSM.Cirrus.Worker;

public sealed class PurgeOptions
{
    public const string SectionName = "Purge";

    public bool Enabled { get; init; } = true;

    public int PollingIntervalSeconds { get; init; } = 60;

    public int BatchSize { get; init; } = 10;

    public int MaxConcurrentDeletes { get; init; } = 2;

    public int LeaseDurationMinutes { get; init; } = 30;

    public int InitialRetryDelayMinutes { get; init; } = 5;

    public int MaximumRetryDelayMinutes { get; init; } = 1440;
}
