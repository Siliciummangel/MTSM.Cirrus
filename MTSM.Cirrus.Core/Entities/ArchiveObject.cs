using MTSM.Cirrus.Core.Enums;

namespace MTSM.Cirrus.Core.Entities;

public sealed class ArchiveObject
{
    public long ArchiveObjectId { get; set; }

    public long TenantId { get; set; }

    public string? ObjectKey { get; set; }

    public string? StagingObjectKey { get; set; }

    public StorageProcessingStatus StorageProcessingStatus { get; set; }
        = StorageProcessingStatus.Staged;

    public string? StorageProcessingLeaseOwner { get; set; }

    public DateTimeOffset? StorageProcessingLeaseUntil { get; set; }

    public int StorageProcessingAttemptCount { get; set; }

    public DateTimeOffset? StorageProcessingNextAttemptAt { get; set; }

    public DateTimeOffset? StorageProcessingStartedAt { get; set; }

    public DateTimeOffset? StorageProcessingVerifiedAt { get; set; }

    public DateTimeOffset? StorageProcessingCompletedAt { get; set; }

    public string? StorageProcessingErrorCode { get; set; }

    public string? StorageProcessingErrorMessage { get; set; }

    public required string BucketName { get; set; }

    public required string FileType { get; set; }

    public string? MimeType { get; set; }

    public required string SourceSystem { get; set; }

    public string? Partner { get; set; }

    public required string OriginalFilename { get; set; }

    public string? Sha256Hash { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateOnly RetentionUntil { get; set; }

    public int? RetentionPolicyId { get; set; }

    public ArchiveStatus ArchiveStatus { get; set; }
        = ArchiveStatus.Pending;

    public DateTimeOffset? DeletionRequestedAt { get; set; }

    public string? DeletionRequestedBy { get; set; }

    public DateTimeOffset? PurgedAt { get; set; }

    public string? PurgeLeaseOwner { get; set; }

    public DateTimeOffset? PurgeLeaseUntil { get; set; }

    public string? StorageVersionId { get; set; }

    public string? EncryptionKeyId { get; set; }

    public bool IsWormProtected { get; set; }

    public DateTimeOffset? LastIntegrityCheckAt { get; set; }

    public DateTimeOffset? NextIntegrityCheckAt { get; set; }

    public string? IntegrityCheckLeaseOwner { get; set; }

    public DateTimeOffset? IntegrityCheckLeaseUntil { get; set; }

    public required string CreatedBy { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public RetentionPolicy? RetentionPolicy { get; set; }

    public ICollection<ArchiveBusinessReference> BusinessReferences { get; set; }
        = new List<ArchiveBusinessReference>();

    public ICollection<ArchiveEvent> Events { get; set; }
        = new List<ArchiveEvent>();

    public ICollection<ArchiveErrorQueueItem> Errors { get; set; }
        = new List<ArchiveErrorQueueItem>();
}
