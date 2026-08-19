using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Admin.Infrastructure;

public static class AdminAuditEvent
{
    public static SecurityAuditEvent Create(
        MachineIdentity machine,
        string eventType,
        string actor,
        string? keyId = null,
        string? details = null) => new()
        {
            TenantId = machine.TenantId,
            MachineIdentity = machine,
            EventType = eventType,
            Actor = actor,
            KeyId = keyId,
            Details = details,
            EventTimestamp = DateTimeOffset.UtcNow
        };
}
