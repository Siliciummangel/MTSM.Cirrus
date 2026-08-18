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

    private static byte[] CreatePayload()
    {
        byte[] payload = new byte[128 * 1024];
        Random.Shared.NextBytes(payload);
        return payload;
    }
}
