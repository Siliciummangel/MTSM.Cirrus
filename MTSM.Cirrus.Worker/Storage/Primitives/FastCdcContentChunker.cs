using FastCdc.Net;
using System.Runtime.CompilerServices;

namespace MTSM.Cirrus.Worker.StorageV2;

public sealed class FastCdcContentChunker : IContentChunker
{
    public async IAsyncEnumerable<ContentChunkData> ChunkAsync(
        Stream source,
        ChunkingProfile profile,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        Validate(profile);

        byte[] window = new byte[profile.MaximumSizeBytes];
        int buffered = 0;
        int sequence = 0;
        long offset = 0;
        bool endOfStream = false;

        while (buffered > 0 || !endOfStream)
        {
            while (!endOfStream && buffered < window.Length)
            {
                int read = await source.ReadAsync(
                    window.AsMemory(buffered, window.Length - buffered),
                    cancellationToken);
                if (read == 0)
                {
                    endOfStream = true;
                    break;
                }

                buffered += read;
            }

            if (buffered == 0)
            {
                yield break;
            }

            byte[] candidate = window.AsSpan(0, buffered).ToArray();
            var chunker = new FastCdc.Net.FastCdc(
                candidate,
                checked((uint)profile.MinimumSizeBytes),
                checked((uint)profile.AverageSizeBytes),
                checked((uint)profile.MaximumSizeBytes),
                profile.Normalized);

            Chunk first = chunker.GetChunks().First();
            int length = checked((int)first.Length);
            if (!endOfStream && length == buffered && buffered < window.Length)
            {
                throw new InvalidOperationException("FastCDC produced an incomplete streaming boundary.");
            }

            byte[] bytes = window.AsSpan(0, length).ToArray();
            yield return new ContentChunkData(sequence++, offset, bytes);
            offset += length;

            int remaining = buffered - length;
            if (remaining > 0)
            {
                Buffer.BlockCopy(window, length, window, 0, remaining);
            }
            buffered = remaining;
        }
    }

    private static void Validate(ChunkingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.MinimumSizeBytes <= 0
            || profile.AverageSizeBytes < profile.MinimumSizeBytes
            || profile.MaximumSizeBytes < profile.AverageSizeBytes)
        {
            throw new ArgumentException("The chunking profile is invalid.", nameof(profile));
        }
    }
}
