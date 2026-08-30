using MTSM.Cirrus.Worker.StorageV2;

namespace MTSM.Cirrus.Core.Tests;

public sealed class FastCdcContentChunkerTests
{
    [Fact]
    public async Task ChunkAsync_IsDeterministicAcrossDifferentStreamReadSizes()
    {
        byte[] content = Enumerable.Range(0, 32_768).Select(index => (byte)(index * 31)).ToArray();
        var profile = new ChunkingProfile("FastCDC", 1, 512, 1024, 4096, true);
        var chunker = new FastCdcContentChunker();

        ContentChunkData[] first = await ReadAsync(chunker, new MemoryStream(content), profile);
        ContentChunkData[] second = await ReadAsync(chunker, new ThrottledReadStream(content, 73), profile);

        Assert.Equal(first.Select(x => x.Bytes.Length), second.Select(x => x.Bytes.Length));
        Assert.Equal(content, first.SelectMany(x => x.Bytes).ToArray());
        Assert.All(first, chunk => Assert.InRange(chunk.Bytes.Length, 1, profile.MaximumSizeBytes));
    }

    private static async Task<ContentChunkData[]> ReadAsync(
        IContentChunker chunker, Stream stream, ChunkingProfile profile)
    {
        var chunks = new List<ContentChunkData>();
        await foreach (ContentChunkData chunk in chunker.ChunkAsync(stream, profile, default))
            chunks.Add(chunk);
        return chunks.ToArray();
    }

    private sealed class ThrottledReadStream(byte[] content, int maximumRead)
        : MemoryStream(content, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumRead)], cancellationToken);
    }
}
