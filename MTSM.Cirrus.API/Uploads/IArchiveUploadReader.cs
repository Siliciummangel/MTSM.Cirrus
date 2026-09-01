namespace MTSM.Cirrus.API.Uploads;

public interface IArchiveUploadReader
{
    Task<ArchiveUpload> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default);
}
