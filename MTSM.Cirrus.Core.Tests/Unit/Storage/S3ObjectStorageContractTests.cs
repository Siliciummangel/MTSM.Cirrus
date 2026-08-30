using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Config;
using MTSM.Cirrus.Core.Providers.S3;

namespace MTSM.Cirrus.Core.Tests;

public sealed class S3ObjectStorageContractTests
{
    [Fact]
    public async Task OpenReadRangeAsync_RejectsInvalidRangeBeforeNetworkAccess()
    {
        using S3ObjectStorage storage = CreateStorage();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            storage.OpenReadRangeAsync("bucket", "object", -1, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            storage.OpenReadRangeAsync("bucket", "object", 0, 0));
    }
    [Fact]
    public async Task WriteAsync_RejectsInvalidLocationBeforeNetworkAccess()
    {
        using S3ObjectStorage storage = CreateStorage();
        await using var content = new MemoryStream("content"u8.ToArray());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.WriteAsync(" bucket ", "object", content, null));
    }

    [Fact]
    public async Task WriteAsync_RejectsHeaderInjectionBeforeNetworkAccess()
    {
        using S3ObjectStorage storage = CreateStorage();
        await using var content = new MemoryStream("content"u8.ToArray());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.WriteAsync(
                "bucket",
                "object",
                content,
                "text/plain\r\nX-Injected: true"));
    }

    [Fact]
    public async Task OperationsAfterDispose_AreRejected()
    {
        S3ObjectStorage storage = CreateStorage();
        storage.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            storage.ExistsAsync("bucket", "object"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            storage.DeleteAsync("bucket", "object"));
    }

    private static S3ObjectStorage CreateStorage()
    {
        return new S3ObjectStorage(
            Options.Create(new S3Options
            {
                ServiceUrl = "http://127.0.0.1:1",
                AccessKey = "test-access-key",
                SecretKey = "test-secret-key",
                Region = "us-east-1",
                CreateBucketIfMissing = false
            }),
            NullLogger<S3ObjectStorage>.Instance);
    }
}
