namespace MTSM.Cirrus.Core.Enums;

/// <summary>
/// Represents the lifecycle status of an archived record and its corresponding storage object.
/// </summary>
public enum ArchiveStatus
{
    /// <summary>
    /// Archiving has been initiated but is not yet complete.
    /// </summary>
    Pending,

    /// <summary>
    /// Metadata and S3 object both exist and are fully accessible for regular use.
    /// </summary>
    Active,

    /// <summary>
    /// Archiving or an underlying technical processing step has failed.
    /// </summary>
    Error,

    /// <summary>
    /// The record is soft-deleted and queued for permanent physical removal. 
    /// The underlying S3 object may still be present.
    /// </summary>
    DeletionRequested,

    /// <summary>
    /// The S3 object has been physically deleted. 
    /// The PostgreSQL record may still persist depending on the clean-up policy.
    /// </summary>
    Purged
}
