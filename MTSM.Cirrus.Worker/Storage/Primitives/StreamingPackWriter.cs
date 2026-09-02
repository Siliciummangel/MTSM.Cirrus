using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Models;
using System.Security.Cryptography;
using System.Threading.Channels;

namespace MTSM.Cirrus.Worker.StorageV2;

/// <summary>
/// Single-producer pack upload. Only CompleteAsync publishes end-of-stream;
/// disposing an unfinished writer cancels the upload and waits for its cleanup.
/// </summary>
public sealed class StreamingPackWriter : IAsyncDisposable
{
    // One block being read and one queued block. Appends wait for capacity before
    // copying another block, regardless of chunk, archive or target-pack size.
    public const int BufferBlockSizeBytes = 64 * 1024;
    private readonly Channel<byte[]> _blocks = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
    {
        SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
    });
    private readonly CancellationTokenSource _lifetime;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly Task<ObjectStorageWriteResult> _upload;
    private bool _sealed;
    private bool _disposed;

    public StreamingPackWriter(IObjectStorage storage, string bucketName, string objectKey,
        CancellationToken cancellationToken)
    {
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _upload = UploadAsync(storage, bucketName, objectKey);
    }

    public long Length { get; private set; }

    public async Task<PackEntry> AppendAsync(byte[] bytes, int rawLength, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed) throw new InvalidOperationException("The pack is already sealed.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        long offset = Length;
        try
        {
            for (int position = 0; position < bytes.Length;)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!await _blocks.Writer.WaitToWriteAsync(linked.Token))
                    throw new IOException("The pack upload stopped accepting content.");
                int count = Math.Min(BufferBlockSizeBytes, bytes.Length - position);
                if (!_blocks.Writer.TryWrite(bytes.AsSpan(position, count).ToArray()))
                    throw new IOException("The pack upload stopped accepting content.");
                position += count;
            }
            _hash.AppendData(bytes);
            Length = checked(Length + bytes.Length);
            return new PackEntry(offset, bytes.Length, rawLength);
        }
        catch (Exception exception)
        {
            _sealed = true;
            _blocks.Writer.TryComplete(exception);
            _lifetime.Cancel();
            if (_upload.IsFaulted) await _upload;
            throw;
        }
    }

    public async Task<UploadedPackContent> CompleteAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed) throw new InvalidOperationException("The pack is already sealed.");
        _sealed = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Length == 0) throw new InvalidOperationException("An empty pack cannot be uploaded.");
            _blocks.Writer.TryComplete();
            ObjectStorageWriteResult write = await _upload.WaitAsync(cancellationToken);
            string hash = Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
            return new UploadedPackContent(write, Length, hash);
        }
        catch (Exception exception)
        {
            _blocks.Writer.TryComplete(exception);
            _lifetime.Cancel();
            throw;
        }
    }

    private async Task<ObjectStorageWriteResult> UploadAsync(IObjectStorage storage, string bucketName, string objectKey)
    {
        await using var source = new BlockReadStream(_blocks.Reader);
        try
        {
            ObjectStorageWriteResult result = await storage.WriteAsync(bucketName, objectKey, source,
                "application/vnd.mtsm.cirrus.pack", null, _lifetime.Token);
            if (!source.EndOfStream)
                throw new IOException("The storage provider completed before consuming the pack.");
            return result;
        }
        catch (Exception exception)
        {
            // Wake a producer waiting for buffer capacity if the consumer fails.
            _blocks.Writer.TryComplete(exception);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _blocks.Writer.TryComplete(new OperationCanceledException("The pack writer was disposed."));
        try { await _upload; }
        catch { /* Append/Complete reports upload errors; cleanup must preserve the caller's failure. */ }
        _hash.Dispose();
        _lifetime.Dispose();
    }

    private sealed class BlockReadStream(ChannelReader<byte[]> reader) : Stream
    {
        private ReadOnlyMemory<byte> _current;
        public bool EndOfStream { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length == 0) return 0;
            while (_current.IsEmpty)
            {
                if (!await reader.WaitToReadAsync(cancellationToken))
                {
                    EndOfStream = true;
                    return 0;
                }
                if (reader.TryRead(out byte[]? block)) _current = block;
            }
            int count = Math.Min(buffer.Length, _current.Length);
            _current[..count].CopyTo(buffer);
            _current = count == _current.Length ? default : _current[count..];
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public sealed record PackEntry(long Offset, int StoredLength, int RawLength);
public sealed record UploadedPackContent(ObjectStorageWriteResult Write, long Length, string Sha256Hash);
