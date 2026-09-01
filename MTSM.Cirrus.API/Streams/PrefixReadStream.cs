namespace MTSM.Cirrus.API.Streams;

internal sealed class PrefixReadStream : Stream
{
    private readonly Stream _inner;
    private readonly byte _prefix;
    private bool _prefixRead;

    public PrefixReadStream(byte prefix, Stream inner)
    {
        _prefix = prefix;
        _inner = inner;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBuffer(buffer, offset, count);

        if (!_prefixRead && count > 0)
        {
            buffer[offset] = _prefix;
            _prefixRead = true;
            return 1;
        }

        return _inner.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        if (!_prefixRead && !buffer.IsEmpty)
        {
            buffer[0] = _prefix;
            _prefixRead = true;
            return 1;
        }

        return _inner.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (!_prefixRead && !buffer.IsEmpty)
        {
            buffer.Span[0] = _prefix;
            _prefixRead = true;
            return 1;
        }

        return await _inner.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    private static void ValidateBuffer(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (buffer.Length - offset < count)
        {
            throw new ArgumentException("The offset and count exceed the buffer length.");
        }
    }
}
