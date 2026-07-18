using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace MTSM.Cirrus.API.Contracts.Requests;

public sealed class ArchiveUploadRequest
{
    [Required]
    [FromForm(Name = "file")]
    public IFormFile File { get; init; } = null!;

    [Required]
    [StringLength(100)]
    [FromForm(Name = "fileType")]
    public string FileType { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    [FromForm(Name = "sourceSystem")]
    public string SourceSystem { get; init; } = string.Empty;

    [StringLength(200)]
    [FromForm(Name = "partner")]
    public string? Partner { get; init; }

    [Required]
    [StringLength(100)]
    [FromForm(Name = "tenant")]
    public string Tenant { get; init; } = string.Empty;

    [FromForm(Name = "receivedAt")]
    public DateTimeOffset? ReceivedAt { get; init; }

    [Required]
    [StringLength(200)]
    [FromForm(Name = "createdBy")]
    public string CreatedBy { get; init; } = string.Empty;

    [FromForm(Name = "retentionPolicyId")]
    public int? RetentionPolicyId { get; init; }

    [FromForm(Name = "retentionUntil")]
    public DateOnly? RetentionUntil { get; init; }

    [FromForm(Name = "businessReferences")]
    public List<ArchiveBusinessReferenceRequest> BusinessReferences { get; init; }
        = [];
}