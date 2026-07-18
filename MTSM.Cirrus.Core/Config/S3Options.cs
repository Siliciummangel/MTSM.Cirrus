namespace MTSM.Cirrus.Core.Config;

public sealed class S3Options
{
    public const string SectionName = "S3";

    /// <summary>
    /// S3 endpoint, for example https://s3.example.internal.
    /// </summary>
    public required string ServiceUrl { get; init; }

    public required string AccessKey { get; init; }

    public required string SecretKey { get; init; }

    /// <summary>
    /// Signing region used for AWS Signature Version 4.
    /// For S3-compatible storage this is commonly us-east-1.
    /// </summary>
    public string Region { get; init; } = "us-east-1";

    /// <summary>
    /// Required by most S3-compatible object stores.
    /// Produces URLs in the form endpoint/bucket/object.
    /// </summary>
    public bool ForcePathStyle { get; init; } = true;

    /// <summary>
    /// Creates the archive bucket during the first write when it does not exist.
    /// </summary>
    public bool CreateBucketIfMissing { get; init; } = true;
}
