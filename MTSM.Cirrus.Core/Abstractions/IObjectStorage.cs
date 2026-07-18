using MTSM.Cirrus.Core.Models;

namespace MTSM.Cirrus.Core.Abstractions;

public interface IObjectStorage
{
    Task<ObjectStorageWriteResult> WriteAsync(
        string bucketName,
        string objectKey,
        Stream content,
        string? contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default);
}