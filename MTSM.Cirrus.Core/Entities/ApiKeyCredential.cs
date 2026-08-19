using MTSM.Cirrus.Core.Enums;

namespace MTSM.Cirrus.Core.Entities;

public sealed class ApiKeyCredential
{
    public long ApiKeyCredentialId { get; set; }
    public long MachineIdentityId { get; set; }
    public required string KeyId { get; set; }
    public required byte[] SecretHash { get; set; }
    public required string HashAlgorithm { get; set; }
    public ApiKeyStatus Status { get; set; } = ApiKeyStatus.Active;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public long? ReplacedById { get; set; }
    public string? Description { get; set; }
    public MachineIdentity MachineIdentity { get; set; } = null!;
    public ApiKeyCredential? ReplacedBy { get; set; }
}
