using MTSM.Cirrus.API.Contracts.Requests;

namespace MTSM.Cirrus.API.Uploads;

public sealed record ArchiveUpload(
    ArchiveUploadMetadataRequest Metadata,
    Stream Content,
    string OriginalFilename,
    string? ContentType);
