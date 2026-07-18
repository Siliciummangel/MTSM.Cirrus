using System.ComponentModel.DataAnnotations;

namespace MTSM.Cirrus.API.Contracts.Requests;

public sealed class ArchiveBusinessReferenceRequest
{
    [Range(1, int.MaxValue)]
    public int BusinessReferenceTypeId { get; init; }

    [Required]
    [StringLength(500)]
    public string ReferenceValue { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string BusinessType { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Tenant { get; init; } = string.Empty;
}