namespace MTSM.Cirrus.API.Config;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// Maximum allowed size of a multiplart upload request in bytes. Default is 1 GiB.
    /// </summary>
    public long MaxMultipartUploadSizeBytes { get; set; } = 1024 * 1024 * 1024;
}