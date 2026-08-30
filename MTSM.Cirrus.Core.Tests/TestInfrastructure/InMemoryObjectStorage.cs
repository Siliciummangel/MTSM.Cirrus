using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Exceptions;
using MTSM.Cirrus.Core.Models;
using System.Collections.Concurrent;

namespace MTSM.Cirrus.Core.Tests.TestInfrastructure;

internal sealed class InMemoryObjectStorage : IObjectStorage
{
    private readonly ConcurrentDictionary<(string Bucket, string Key), byte[]> _objects = new();

    public Exception? WriteException { get; set; }

    public Exception? ReadException { get; set; }

    public Exception? DeleteException { get; set; }

    public Func<CancellationToken, Task>? BeforeDeleteCompletesAsync { get; set; }

    public int DeleteCallCount { get; private set; }

    public Func<CancellationToken, Task>? BeforeWriteCompletesAsync { get; set; }

    public TrackingMemoryStream? LastReadStream { get; private set; }

    public async Task<ObjectStorageWriteResult> WriteAsync(
        string bucketName,
        string objectKey,
        Stream content,
        string? contentType,
        string? encryptionKeyId = null,
        CancellationToken cancellationToken = default)
    {
        if (WriteException is not null)
        {
            throw WriteException;
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        if (BeforeWriteCompletesAsync is not null)
        {
            await BeforeWriteCompletesAsync(cancellationToken);
        }

        _objects[(bucketName, objectKey)] = buffer.ToArray();
        return new ObjectStorageWriteResult("version-1", "etag-1");
    }

    public Task<Stream> OpenReadAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ReadException is not null)
        {
            throw ReadException;
        }

        if (!_objects.TryGetValue((bucketName, objectKey), out byte[]? content))
        {
            throw new InvalidOperationException("The requested test object does not exist.");
        }

        LastReadStream = new TrackingMemoryStream(content);
        return Task.FromResult<Stream>(LastReadStream);
    }

    public Task<Stream> OpenReadRangeAsync(
        string bucketName,
        string objectKey,
        long offset,
        long length,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        if (!_objects.TryGetValue((bucketName, objectKey), out byte[]? content)
            || offset > content.LongLength
            || length > content.LongLength - offset)
        {
            throw new ObjectStorageException("Object or requested range was not found.");
        }

        var result = new byte[checked((int)length)];
        Buffer.BlockCopy(content, checked((int)offset), result, 0, result.Length);
        return Task.FromResult<Stream>(new MemoryStream(result, writable: false));
    }

    public Task<bool> ExistsAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_objects.ContainsKey((bucketName, objectKey)));
    }

    public async Task<ObjectStorageDeleteOutcome> DeleteAsync(
        string bucketName,
        string objectKey,
        string? versionId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteCallCount++;

        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        if (BeforeDeleteCompletesAsync is not null)
        {
            await BeforeDeleteCompletesAsync(cancellationToken);
        }

        return _objects.TryRemove((bucketName, objectKey), out _)
            ? ObjectStorageDeleteOutcome.Deleted
            : ObjectStorageDeleteOutcome.NotFound;
    }

    public void Replace(string bucketName, string objectKey, byte[] content)
    {
        _objects[(bucketName, objectKey)] = content;
    }
}

internal sealed class TrackingMemoryStream(byte[] content) : MemoryStream(content)
{
    public bool IsDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
