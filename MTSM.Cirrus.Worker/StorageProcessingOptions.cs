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
    public int MinimumChunkSizeBytes { get; init; } = 512 * 1024;
    public int AverageChunkSizeBytes { get; init; } = 2 * 1024 * 1024;
    public int MaximumChunkSizeBytes { get; init; } = 8 * 1024 * 1024;
    public long TargetPackSizeBytes { get; init; } = 256L * 1024 * 1024;
    public int MaximumBatchWaitSeconds { get; init; } = 15;
    public int LeaseHeartbeatSeconds { get; init; } = 30;
    public int ZstdCompressionLevel { get; init; } = 3;
    public bool PackMaintenanceEnabled { get; init; } = true;
    public int PackMaintenanceBatchSize { get; init; } = 10;
    public int OrphanGracePeriodMinutes { get; init; } = 60;
    public int CompactionMinimumAgeMinutes { get; init; } = 60;
    public int CompactionUtilizationPercent { get; init; } = 70;
}
