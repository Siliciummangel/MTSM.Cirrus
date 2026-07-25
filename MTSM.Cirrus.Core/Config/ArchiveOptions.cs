namespace MTSM.Cirrus.Core.Config;

public sealed class ArchiveOptions
{
    public const string SectionName = "Archive";

    public required string BucketName { get; init; }

    public string ObjectKeyPrefix { get; init; } = "objects";
}