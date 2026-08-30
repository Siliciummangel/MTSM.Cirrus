namespace MTSM.Cirrus.Core.Abstractions;

public interface IManifestContentReader
{
    Task<Stream> OpenReadAsync(
        long contentManifestId,
        CancellationToken cancellationToken = default);
}
