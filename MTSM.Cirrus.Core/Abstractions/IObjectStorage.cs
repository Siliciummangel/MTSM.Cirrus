using MTSM.Cirrus.Core.Models;

namespace MTSM.Cirrus.Core.Abstractions;

public interface IObjectStorage
{
    /// <summary>
    /// Writes the stream from its current position through end-of-stream.
    /// The caller retains ownership of the stream.
    /// </summary>
    Task<ObjectStorageWriteResult> WriteAsync(
        string bucketName,
        string objectKey,
        Stream content,
        string? contentType,
        string? encryptionKeyId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a readable stream. The caller owns and must dispose the returned stream.
    /// </summary>
    Task<Stream> OpenReadAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default);
}
