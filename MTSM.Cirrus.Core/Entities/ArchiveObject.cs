using MTSM.Cirrus.Core.Enums;

namespace MTSM.Cirrus.Core.Entities;

public sealed class ArchiveObject
{
    public long ArchiveObjectId { get; set; }

    public required string ObjectKey { get; set; }

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

    public ArchiveStatus ArchiveStatus { get; set; } = ArchiveStatus.Pending;

    public string? StorageVersionId { get; set; }

    public string? EncryptionKeyId { get; set; }

    public bool IsWormProtected { get; set; }

    public required string CreatedBy { get; set; }

    public RetentionPolicy? RetentionPolicy { get; set; }

    public ICollection<ArchiveBusinessReference> BusinessReferences { get; set; }
        = new List<ArchiveBusinessReference>();

    public ICollection<ArchiveEvent> Events { get; set; }
        = new List<ArchiveEvent>();

    public ICollection<ArchiveErrorQueueItem> Errors { get; set; }
        = new List<ArchiveErrorQueueItem>();
}