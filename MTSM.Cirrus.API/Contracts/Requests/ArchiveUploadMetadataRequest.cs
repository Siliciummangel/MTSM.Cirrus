using System.ComponentModel.DataAnnotations;

namespace MTSM.Cirrus.API.Contracts.Requests;

public sealed class ArchiveUploadMetadataRequest
{
    [Required]
    [StringLength(100)]
    public string FileType { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string SourceSystem { get; init; } = string.Empty;

    [StringLength(200)]
    public string? Partner { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }

    public int? RetentionPolicyId { get; init; }

    public DateOnly? RetentionUntil { get; init; }

    public List<ArchiveBusinessReferenceRequest> BusinessReferences { get; init; }
        = [];
}
