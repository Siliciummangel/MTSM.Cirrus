namespace MTSM.Cirrus.Core.Entities;

public sealed class ManifestChunk
{
    public long ContentManifestId { get; set; }
    public int SequenceNumber { get; set; }
    public long ContentChunkId { get; set; }
    public long OriginalOffset { get; set; }
    public int RawLength { get; set; }

    public ContentManifest ContentManifest { get; set; } = null!;
    public ContentChunk ContentChunk { get; set; } = null!;
}
