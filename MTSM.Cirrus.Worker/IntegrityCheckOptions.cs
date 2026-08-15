namespace MTSM.Cirrus.Worker;

public sealed class IntegrityCheckOptions
{
    public const string SectionName = "IntegrityChecks";

    public bool Enabled { get; init; } = true;

    public int InitialVerificationDelayHours { get; init; } = 24;

    public int ReverificationIntervalDays { get; init; } = 180;

    public int FailureRetryDelayMinutes { get; init; } = 60;

    public int PollingIntervalSeconds { get; init; } = 60;

    public int BatchSize { get; init; } = 10;

    public int MaxConcurrentChecks { get; init; } = 2;

    public int LeaseDurationMinutes { get; init; } = 30;

    public string? WorkerInstanceId { get; init; }
}
