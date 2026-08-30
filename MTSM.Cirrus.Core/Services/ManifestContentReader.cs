using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Exceptions;
using System.Security.Cryptography;
using ZstdSharp;

namespace MTSM.Cirrus.Core.Services;

public sealed class ManifestContentReader(
    CirrusDbContext dbContext,
    IObjectStorage storage) : IManifestContentReader
{
    private sealed record ChunkReadLocation(
        int SequenceNumber,
        string ExpectedHash,
        int RawLength,
        string BucketName,
        string ObjectKey,
        long PackOffset,
        int StoredLength,
        string CompressionAlgorithm);

    private sealed record ManifestDescriptor(
        int FormatVersion,
        string HashAlgorithm,
        int ChunkCount);

    public async Task<Stream> OpenReadAsync(
        long contentManifestId,
        CancellationToken cancellationToken = default)
    {
        ManifestDescriptor descriptor = await dbContext.ContentManifests
            .AsNoTracking()
            .Where(item => item.ContentManifestId == contentManifestId)
            .Select(item => new ManifestDescriptor(
                item.ManifestFormatVersion,
                item.HashAlgorithm,
                item.ChunkCount))
            .SingleAsync(cancellationToken);

        if (descriptor.FormatVersion != 1
            || !string.Equals(descriptor.HashAlgorithm, "SHA-256", StringComparison.Ordinal))
        {
            throw new ArchiveException($"Content manifest {contentManifestId} uses an unsupported format.");
        }

        ChunkReadLocation[] chunks = await dbContext.ManifestChunks
            .AsNoTracking()
            .Where(item => item.ContentManifestId == contentManifestId)
            .OrderBy(item => item.SequenceNumber)
            .Select(item => new ChunkReadLocation(
                item.SequenceNumber,
                item.ContentChunk.ChunkHash,
                item.RawLength,
                item.ContentChunk.StorageLocations
                    .Where(location => location.StoragePack.PackStatus == PackStatus.Committed)
                    .OrderBy(location => location.StorageLocationId)
                    .Select(location => location.StoragePack.BucketName)
                    .First(),
                item.ContentChunk.StorageLocations
                    .Where(location => location.StoragePack.PackStatus == PackStatus.Committed)
                    .OrderBy(location => location.StorageLocationId)
                    .Select(location => location.StoragePack.ObjectKey)
                    .First(),
                item.ContentChunk.StorageLocations
                    .Where(location => location.StoragePack.PackStatus == PackStatus.Committed)
                    .OrderBy(location => location.StorageLocationId)
                    .Select(location => location.PackOffset)
                    .First(),
                item.ContentChunk.StorageLocations
                    .Where(location => location.StoragePack.PackStatus == PackStatus.Committed)
                    .OrderBy(location => location.StorageLocationId)
                    .Select(location => location.StoredLength)
                    .First(),
                item.ContentChunk.StorageLocations
                    .Where(location => location.StoragePack.PackStatus == PackStatus.Committed)
                    .OrderBy(location => location.StorageLocationId)
                    .Select(location => location.CompressionAlgorithm)
                    .First()))
            .ToArrayAsync(cancellationToken);

        if (chunks.Length == 0 || chunks.Length != descriptor.ChunkCount)
        {
            throw new ArchiveException($"Content manifest {contentManifestId} contains no readable chunks.");
        }

        return new ManifestReconstructionStream(storage, chunks);
    }

    private sealed class ManifestReconstructionStream(
        IObjectStorage storage,
        IReadOnlyList<ChunkReadLocation> chunks) : Stream
    {
        private int _chunkIndex;
        private MemoryStream? _currentChunk;
        private bool _disposed;

        public override bool CanRead => !_disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => chunks.Sum(chunk => (long)chunk.RawLength);
        public override long Position { get; set; }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (buffer.Length == 0)
            {
                return 0;
            }

            while (_currentChunk is null || _currentChunk.Position == _currentChunk.Length)
            {
                _currentChunk?.Dispose();
                _currentChunk = null;
                if (_chunkIndex >= chunks.Count)
                {
                    return 0;
                }

                ChunkReadLocation location = chunks[_chunkIndex++];
                await using Stream range = await storage.OpenReadRangeAsync(
                    location.BucketName,
                    location.ObjectKey,
                    location.PackOffset,
                    location.StoredLength,
                    cancellationToken);
                var memory = new MemoryStream(location.RawLength);
                await range.CopyToAsync(memory, cancellationToken);
                byte[] stored = memory.ToArray();
                memory.Dispose();
                byte[] bytes = location.CompressionAlgorithm switch
                {
                    "None" => stored,
                    "Zstd" => Decompress(stored, location.RawLength),
                    _ => throw new ArchiveException($"Compression algorithm '{location.CompressionAlgorithm}' is not supported.")
                };

                string actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (bytes.Length != location.RawLength
                    || !string.Equals(actualHash, location.ExpectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArchiveException($"Chunk {location.SequenceNumber} failed integrity verification.");
                }

                _currentChunk = new MemoryStream(bytes, writable: false);
            }

            int read = await _currentChunk.ReadAsync(buffer, cancellationToken);
            Position += read;
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _currentChunk?.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private static byte[] Decompress(byte[] stored, int rawLength)
        {
            using var decompressor = new Decompressor();
            return decompressor.Unwrap(stored, rawLength).ToArray();
        }
    }
}
