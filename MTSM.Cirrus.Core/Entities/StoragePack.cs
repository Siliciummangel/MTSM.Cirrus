using MTSM.Cirrus.Core.Enums;

namespace MTSM.Cirrus.Core.Entities;

public sealed class StoragePack
{
    public long StoragePackId { get; set; }
    public long TenantId { get; set; }
    public required string BucketName { get; set; }
    public required string ObjectKey { get; set; }
    public string? StorageVersionId { get; set; }
    public int PackFormatVersion { get; set; } = 1;
    public required string HashAlgorithm { get; set; }
    public string? PackHash { get; set; }
    public long SizeBytes { get; set; }
    public PackStatus PackStatus { get; set; } = PackStatus.Building;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public string? MaintenanceLeaseOwner { get; set; }
    public DateTimeOffset? MaintenanceLeaseUntil { get; set; }
    public int MaintenanceAttempts { get; set; }
    public string? MaintenanceError { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<StorageLocation> StorageLocations { get; set; } = [];
}
