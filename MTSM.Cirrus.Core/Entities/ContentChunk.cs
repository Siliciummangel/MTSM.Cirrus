namespace MTSM.Cirrus.Core.Entities;

public sealed class ContentChunk
{
    public long ContentChunkId { get; set; }
    public long TenantId { get; set; }
    public required string HashAlgorithm { get; set; }
    public required string ChunkHash { get; set; }
    public long RawSizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<ManifestChunk> ManifestChunks { get; set; } = [];
    public ICollection<StorageLocation> StorageLocations { get; set; } = [];
}
