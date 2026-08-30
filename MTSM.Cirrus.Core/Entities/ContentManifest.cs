namespace MTSM.Cirrus.Core.Entities;

public sealed class ContentManifest
{
    public long ContentManifestId { get; set; }
    public long TenantId { get; set; }
    public int ManifestFormatVersion { get; set; } = 1;
    public required string HashAlgorithm { get; set; }
    public required string OriginalHash { get; set; }
    public long OriginalSizeBytes { get; set; }
    public required string ChunkingAlgorithm { get; set; }
    public int ChunkingAlgorithmVersion { get; set; }
    public int MinimumChunkSizeBytes { get; set; }
    public int AverageChunkSizeBytes { get; set; }
    public int MaximumChunkSizeBytes { get; set; }
    public int ChunkCount { get; set; }
    public DateTimeOffset CommittedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<ArchiveObject> ArchiveObjects { get; set; } = [];
    public ICollection<ManifestChunk> Chunks { get; set; } = [];
}
