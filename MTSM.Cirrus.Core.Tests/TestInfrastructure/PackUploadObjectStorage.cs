using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Models;

namespace MTSM.Cirrus.Core.Tests.TestInfrastructure;

// Reads the seeded staging/old packs while exercising the real multipart adapter for new packs.
internal sealed class PackUploadObjectStorage(IObjectStorage source, IObjectStorage uploads) : IObjectStorage
{
    public Task<ObjectStorageWriteResult> WriteAsync(string bucketName, string objectKey, Stream content,
        string? contentType, string? encryptionKeyId = null, CancellationToken cancellationToken = default) =>
        uploads.WriteAsync(bucketName, objectKey, content, contentType, encryptionKeyId, cancellationToken);
    public Task<Stream> OpenReadAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default) =>
        source.OpenReadAsync(bucketName, objectKey, cancellationToken);
    public Task<Stream> OpenReadRangeAsync(string bucketName, string objectKey, long offset, long length,
        CancellationToken cancellationToken = default) =>
        source.OpenReadRangeAsync(bucketName, objectKey, offset, length, cancellationToken);
    public Task<bool> ExistsAsync(string bucketName, string objectKey, CancellationToken cancellationToken = default) =>
        source.ExistsAsync(bucketName, objectKey, cancellationToken);
    public Task<ObjectStorageDeleteOutcome> DeleteAsync(string bucketName, string objectKey, string? versionId = null,
        CancellationToken cancellationToken = default) =>
        source.DeleteAsync(bucketName, objectKey, versionId, cancellationToken);
}
