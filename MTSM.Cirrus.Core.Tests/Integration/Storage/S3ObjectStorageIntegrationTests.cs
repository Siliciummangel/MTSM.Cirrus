using MTSM.Cirrus.Core.Exceptions;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Core.Providers.S3;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;

namespace MTSM.Cirrus.Core.Tests;

[Collection(S3Collection.Name)]
public sealed class S3ObjectStorageIntegrationTests(S3Fixture fixture)
{
    [S3Fact]
    public async Task WriteOpenReadAndExistsAsync_RoundTripsObject()
    {
        using S3ObjectStorage storage = fixture.CreateStorage();
        string objectKey = fixture.CreateObjectKey("payload.bin");
        byte[] expectedContent = CreatePayload();
        await using var source = new MemoryStream(expectedContent);

        Assert.False(
            await storage.ExistsAsync(fixture.BucketName, objectKey));

        ObjectStorageWriteResult result = await storage.WriteAsync(
            fixture.BucketName,
            objectKey,
            source,
            "application/octet-stream");

        Assert.True(source.CanRead);
        Assert.False(string.IsNullOrWhiteSpace(result.ETag));
        Assert.True(
            await storage.ExistsAsync(fixture.BucketName, objectKey));

        await using Stream storedContent = await storage.OpenReadAsync(
            fixture.BucketName,
            objectKey);
        using var destination = new MemoryStream();
        await storedContent.CopyToAsync(destination);

        Assert.Equal(expectedContent, destination.ToArray());

        await using Stream range = await storage.OpenReadRangeAsync(
            fixture.BucketName,
            objectKey,
            1024,
            4096);
        using var rangeDestination = new MemoryStream();
        await range.CopyToAsync(rangeDestination);
        Assert.Equal(expectedContent.AsSpan(1024, 4096).ToArray(), rangeDestination.ToArray());
    }

    [S3Fact]
    public async Task WriteAsync_NonSeekableStreamUsesBoundedMultipartUpload()
    {
        using S3ObjectStorage storage = fixture.CreateStorage();
        string objectKey = fixture.CreateObjectKey("streamed-payload.bin");
        byte[] expectedContent = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(expectedContent);
        await using var memory = new MemoryStream(expectedContent);
        await using var source = new NonSeekableReadStream(memory);

        ObjectStorageWriteResult result = await storage.WriteAsync(
            fixture.BucketName,
            objectKey,
            source,
            "application/octet-stream");

        Assert.False(string.IsNullOrWhiteSpace(result.ETag));
        await using Stream storedContent = await storage.OpenReadAsync(
            fixture.BucketName,
            objectKey);
        using var destination = new MemoryStream();
        await storedContent.CopyToAsync(destination);
        Assert.Equal(expectedContent, destination.ToArray());
    }

    [S3Fact]
    public async Task OpenReadAsync_MissingObjectWrapsProviderFailure()
    {
        using S3ObjectStorage storage = fixture.CreateStorage();
        string existingObjectKey = fixture.CreateObjectKey("existing.bin");
        await using var content = new MemoryStream("content"u8.ToArray());

        await storage.WriteAsync(
            fixture.BucketName,
            existingObjectKey,
            content,
            null);

        string missingObjectKey = fixture.CreateObjectKey("missing.bin");

        ObjectStorageException exception =
            await Assert.ThrowsAsync<ObjectStorageException>(() =>
                storage.OpenReadAsync(
                    fixture.BucketName,
                    missingObjectKey));

        Assert.Equal(
            "The object-storage operation 'read' failed.",
            exception.Message);
        Assert.False(
            await storage.ExistsAsync(
                fixture.BucketName,
                missingObjectKey));
    }

    [S3Fact]
    public async Task DeleteAsync_DeletesExistingObjectAndTreatsMissingAsIdempotentOutcome()
    {
        using S3ObjectStorage storage = fixture.CreateStorage();
        string objectKey = fixture.CreateObjectKey("delete.bin");
        await using var content = new MemoryStream("content"u8.ToArray());
        ObjectStorageWriteResult write = await storage.WriteAsync(
            fixture.BucketName,
            objectKey,
            content,
            null);

        Assert.Equal(
            ObjectStorageDeleteOutcome.Deleted,
            await storage.DeleteAsync(fixture.BucketName, objectKey, write.VersionId));
        Assert.False(await storage.ExistsAsync(fixture.BucketName, objectKey));
        Assert.Equal(
            ObjectStorageDeleteOutcome.NotFound,
            await storage.DeleteAsync(fixture.BucketName, objectKey, write.VersionId));
    }

    private static byte[] CreatePayload()
    {
        byte[] payload = new byte[128 * 1024];
        Random.Shared.NextBytes(payload);
        return payload;
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
