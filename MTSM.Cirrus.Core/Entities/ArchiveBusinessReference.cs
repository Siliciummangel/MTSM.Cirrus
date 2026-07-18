namespace MTSM.Cirrus.Core.Entities;

public sealed class ArchiveBusinessReference
{
    public long ArchiveObjectId { get; set; }

    public int BusinessReferenceTypeId { get; set; }

    public required string ReferenceValue { get; set; }

    public required string BusinessType { get; set; }

    public required string Tenant { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ArchiveObject ArchiveObject { get; set; } = null!;

    public BusinessReferenceType BusinessReferenceType { get; set; } = null!;
}