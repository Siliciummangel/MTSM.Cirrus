namespace MTSM.Cirrus.Core.Entities;

public sealed class ArchiveErrorQueueItem
{
    public long ErrorId { get; set; }

    public long? ArchiveObjectId { get; set; }

    public required string ErrorType { get; set; }

    public DateTimeOffset ErrorTimestamp { get; set; }

    public int RetryCount { get; set; }

    public required string LastErrorMessage { get; set; }

    public DateTimeOffset? NextRetryAt { get; set; }

    public bool Resolved { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public ArchiveObject? ArchiveObject { get; set; }
}