namespace MTSM.Cirrus.Core.Entities;

public sealed class SecurityAuditEvent
{
    public long SecurityAuditEventId { get; set; }
    public long TenantId { get; set; }
    public long MachineIdentityId { get; set; }
    public required string EventType { get; set; }
    public required string Actor { get; set; }
    public string? KeyId { get; set; }
    public DateTimeOffset EventTimestamp { get; set; }
    public string? Details { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public MachineIdentity MachineIdentity { get; set; } = null!;
}
