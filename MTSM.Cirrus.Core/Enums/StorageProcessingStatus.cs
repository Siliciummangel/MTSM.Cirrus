namespace MTSM.Cirrus.Core.Enums;

public enum StorageProcessingStatus
{
    Staged,
    Processing,
    Ready,
    RetryPending,
    CleanupPending,
    Completed,
    Failed
}
