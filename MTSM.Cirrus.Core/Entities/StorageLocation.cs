namespace MTSM.Cirrus.Core.Entities;

public sealed class StorageLocation
{
    public long StorageLocationId { get; set; }
    public long ContentChunkId { get; set; }
    public long StoragePackId { get; set; }
    public long PackOffset { get; set; }
    public int StoredLength { get; set; }
    public int RawLength { get; set; }
    public required string CompressionAlgorithm { get; set; }
    public int CompressionVersion { get; set; }
    public int StorageFormatVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }

    public ContentChunk ContentChunk { get; set; } = null!;
    public StoragePack StoragePack { get; set; } = null!;
}
