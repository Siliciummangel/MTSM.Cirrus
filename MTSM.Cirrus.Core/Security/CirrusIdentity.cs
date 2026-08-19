namespace MTSM.Cirrus.Core.Security;

public sealed record CirrusIdentity(
    long TenantId,
    string Subject,
    string Actor,
    string AuthenticationProvider,
    IReadOnlySet<CirrusPermission> Permissions);
