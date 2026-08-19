using MTSM.Cirrus.Core.Security;

namespace MTSM.Cirrus.API.Security;

public interface ICirrusIdentityAccessor
{
    CirrusIdentity GetRequiredIdentity();
}

public sealed class HttpCirrusIdentityAccessor(IHttpContextAccessor httpContextAccessor)
    : ICirrusIdentityAccessor
{
    public CirrusIdentity GetRequiredIdentity()
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP identity is available.");

        string subject = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("The authenticated identity has no subject.");
        string actor = principal.FindFirst(CirrusClaimTypes.Actor)?.Value
            ?? throw new InvalidOperationException("The authenticated identity has no actor.");
        string provider = principal.FindFirst(CirrusClaimTypes.Provider)?.Value
            ?? throw new InvalidOperationException("The authenticated identity has no provider.");
        string tenantValue = principal.FindFirst(CirrusClaimTypes.TenantId)?.Value
            ?? throw new InvalidOperationException("The authenticated identity has no tenant.");

        if (!long.TryParse(tenantValue, out long tenantId) || tenantId <= 0)
            throw new InvalidOperationException("The authenticated identity has an invalid tenant.");

        HashSet<CirrusPermission> permissions = principal.FindAll(CirrusClaimTypes.Permission)
            .Select(claim => claim.Value switch
            {
                CirrusPermissionNames.ArchiveRead => CirrusPermission.ArchiveRead,
                CirrusPermissionNames.ArchiveWrite => CirrusPermission.ArchiveWrite,
                CirrusPermissionNames.ArchiveDelete => CirrusPermission.ArchiveDelete,
                CirrusPermissionNames.ArchiveVerify => CirrusPermission.ArchiveVerify,
                _ => throw new InvalidOperationException("The authenticated identity has an unknown permission.")
            }).ToHashSet();

        return new CirrusIdentity(tenantId, subject, actor, provider, permissions);
    }
}
