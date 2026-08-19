using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Security;

namespace MTSM.Cirrus.Core.Entities;

public sealed class MachineIdentity
{
    public long MachineIdentityId { get; set; }
    public long TenantId { get; set; }
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public MachineIdentityStatus Status { get; set; } = MachineIdentityStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public ICollection<MachineIdentityPermission> Permissions { get; set; } = [];
    public ICollection<ApiKeyCredential> ApiKeyCredentials { get; set; } = [];
}

public sealed class MachineIdentityPermission
{
    public long MachineIdentityId { get; set; }
    public CirrusPermission Permission { get; set; }
    public MachineIdentity MachineIdentity { get; set; } = null!;
}
