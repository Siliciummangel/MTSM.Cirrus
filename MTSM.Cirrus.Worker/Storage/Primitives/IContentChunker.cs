namespace MTSM.Cirrus.Worker.StorageV2;

public interface IContentChunker
{
    IAsyncEnumerable<ContentChunkData> ChunkAsync(
        Stream source,
        ChunkingProfile profile,
        CancellationToken cancellationToken);
}
