using System.Security.Cryptography;

namespace MTSM.Cirrus.Worker.StorageV2;

public sealed class TemporaryPackBuilder : IAsyncDisposable
{
    private readonly string _path = Path.GetTempFileName();
    private readonly FileStream _stream;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private bool _sealed;

    public TemporaryPackBuilder()
    {
        _stream = new FileStream(
            _path,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    ~TemporaryPackBuilder()
    {
        _hash.Dispose();
        _stream.Dispose();
        TryDeleteTemporaryFile();
    }

    public long Length => _stream.Length;

    public async Task<PackEntry> AppendAsync(byte[] bytes, int rawLength, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_sealed, this);
        long offset = _stream.Position;
        await _stream.WriteAsync(bytes, cancellationToken);
        _hash.AppendData(bytes);
        return new PackEntry(offset, bytes.Length, rawLength);
    }

    public async Task<SealedPack> SealAsync(CancellationToken cancellationToken)
    {
        if (_sealed)
        {
            throw new InvalidOperationException("The pack is already sealed.");
        }

        _sealed = true;
        await _stream.FlushAsync(cancellationToken);
        string hash = Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        _stream.Position = 0;
        return new SealedPack(_stream, _stream.Length, hash);
    }

    public async ValueTask DisposeAsync()
    {
        _hash.Dispose();
        await _stream.DisposeAsync();
        TryDeleteTemporaryFile();
        GC.SuppressFinalize(this);
    }

    private void TryDeleteTemporaryFile()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // A failed temporary-file cleanup is recoverable by the host OS.
        }
    }
}

public sealed record PackEntry(long Offset, int StoredLength, int RawLength);
public sealed record SealedPack(Stream Content, long Length, string Sha256Hash);
