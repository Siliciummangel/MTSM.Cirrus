using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace MTSM.Cirrus.Core.Streams;

/// <summary>
/// Read-only stream wrapper that calculates a SHA-256 hash
/// and counts the bytes while the underlying stream is read.
/// </summary>
public sealed class HashingReadStream : Stream
{
    private readonly Stream _innerStream;
    private readonly IncrementalHash _incrementalHash;
    private readonly bool _leaveOpen;

    private bool _hashFinalized;
    private string? _hashHex;

    public HashingReadStream(
        Stream innerStream,
        bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(innerStream);

        if (!innerStream.CanRead)
        {
            throw new ArgumentException(
                "The supplied stream must be readable.",
                nameof(innerStream));
        }

        _innerStream = innerStream;
        _leaveOpen = leaveOpen;
        _incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }

    public long BytesRead { get; private set; }

    public string GetHashHex()
    {
        if (!_hashFinalized)
        {
            _hashHex = Convert.ToHexString(
                    _incrementalHash.GetHashAndReset())
                .ToLowerInvariant();

            _hashFinalized = true;
        }

        return _hashHex!;
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count)
    {
        int bytesRead = _innerStream.Read(buffer, offset, count);

        ProcessReadBytes(
            buffer.AsSpan(offset, bytesRead));

        return bytesRead;
    }

    public override int Read(Span<byte> buffer)
    {
        int bytesRead = _innerStream.Read(buffer);

        ProcessReadBytes(buffer[..bytesRead]);

        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int bytesRead = await _innerStream.ReadAsync(
            buffer,
            cancellationToken);

        ProcessReadBytes(buffer.Span[..bytesRead]);

        return bytesRead;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        int bytesRead = await _innerStream.ReadAsync(
            buffer.AsMemory(offset, count),
            cancellationToken);

        ProcessReadBytes(
            buffer.AsSpan(offset, bytesRead));

        return bytesRead;
    }

    private void ProcessReadBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        if (_hashFinalized)
        {
            throw new InvalidOperationException(
                "The hash has already been finalized.");
        }

        _incrementalHash.AppendData(bytes);
        BytesRead += bytes.Length;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _incrementalHash.Dispose();

            if (!_leaveOpen)
            {
                _innerStream.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        _incrementalHash.Dispose();

        if (!_leaveOpen)
        {
            await _innerStream.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    public override bool CanRead => _innerStream.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length =>
        throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override Task FlushAsync(
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public override long Seek(
        long offset,
        SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(
        byte[] buffer,
        int offset,
        int count)
    {
        throw new NotSupportedException();
    }
}