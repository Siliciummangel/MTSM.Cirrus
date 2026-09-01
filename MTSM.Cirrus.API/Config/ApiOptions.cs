namespace MTSM.Cirrus.API.Config;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// Maximum allowed size of a multiplart upload request in bytes. Default is 1 GiB.
    /// </summary>
    public long MaxMultipartUploadSizeBytes { get; set; } = 1024 * 1024 * 1024;
    /// <summary>
    /// Maximum allowed size of the JSON metadata multipart section in bytes.
    /// </summary>
    public int MaxUploadMetadataSizeBytes { get; set; } = 64 * 1024;
    public int RateLimitPermitCount { get; set; } = 300;
    public int RateLimitWindowSeconds { get; set; } = 60;
}
