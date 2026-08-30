using MTSM.Cirrus.Core.Enums;

namespace MTSM.Cirrus.Core.Entities;

public sealed class Tenant
{
    public long TenantId { get; set; }

    public required string DisplayName { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public required string BucketName { get; set; }

    public required string ObjectKeyPrefix { get; set; }

    public string? EncryptionKeyId { get; set; }

    public int? DefaultRetentionPolicyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<ArchiveObject> ArchiveObjects { get; set; }
        = new List<ArchiveObject>();

    public ICollection<ContentManifest> ContentManifests { get; set; } = [];
    public ICollection<ContentChunk> ContentChunks { get; set; } = [];
    public ICollection<StoragePack> StoragePacks { get; set; } = [];

    public RetentionPolicy? DefaultRetentionPolicy { get; set; }
}
