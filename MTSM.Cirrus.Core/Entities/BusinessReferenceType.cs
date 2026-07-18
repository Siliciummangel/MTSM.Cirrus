namespace MTSM.Cirrus.Core.Entities;

public sealed class BusinessReferenceType
{
    public int BusinessReferenceTypeId { get; set; }

    public required string ReferenceTypeKey { get; set; }

    public string? Description { get; set; }

    public ICollection<ArchiveBusinessReference> BusinessReferences { get; set; }
        = new List<ArchiveBusinessReference>();
}
