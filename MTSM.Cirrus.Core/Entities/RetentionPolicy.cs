namespace MTSM.Cirrus.Core.Entities;

public sealed class RetentionPolicy
{
    public int RetentionPolicyId { get; set; }

    public required string PolicyName { get; set; }

    public int RetentionYears { get; set; }

    public bool DeleteAfterExpiry { get; set; }

    public bool WormRequired { get; set; }

    public string? Description { get; set; }

    public ICollection<ArchiveObject> ArchiveObjects { get; set; }
        = new List<ArchiveObject>();
}