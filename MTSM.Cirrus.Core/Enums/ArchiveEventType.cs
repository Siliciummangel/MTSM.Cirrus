namespace MTSM.Cirrus.Core.Enums;

public enum ArchiveEventType
{
    Created,
    Archived,
    Downloaded,
    IntegrityVerified,
    IntegrityCheckFailed,
    ErrorOccurred,
    RetryStarted,
    RetrySucceeded,
    RetentionApplied,
    DeletionRequested,
    Purged,
    PurgeFailed,
    StorageProcessingVerified,
    StorageProcessingFailed
}
