namespace MTSM.Cirrus.Core.Enums;

public enum StorageProcessingStatus
{
    Staged,
    Processing,
    Ready,
    Packing,
    RetryPending,
    CleanupPending,
    Cleaning,
    Completed,
    Failed
}
