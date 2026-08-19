using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Security;

namespace MTSM.Cirrus.API.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder,
    CirrusDbContext dbContext)
    : AuthenticationHandler<ApiKeyOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization.Count > 1)
            return AuthenticateResult.Fail("Invalid API key.");

        string? authorization = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authorization))
            return AuthenticateResult.NoResult();

        const string prefix = ApiKeyOptions.Scheme + " ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        string credential = authorization[prefix.Length..].Trim();
        int separator = credential.IndexOf('.');
        if (separator <= 7 || separator == credential.Length - 1 || credential.Length > 256)
            return AuthenticateResult.Fail("Invalid API key.");

        string publicPart = credential[..separator];
        if (!publicPart.StartsWith("cirrus_", StringComparison.Ordinal))
            return AuthenticateResult.Fail("Invalid API key.");

        string keyId = publicPart[7..];
        string secret = credential[(separator + 1)..];
        if (keyId.Length is < 12 or > 32
            || keyId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            return AuthenticateResult.Fail("Invalid API key.");

        var credentialRecord = await dbContext.ApiKeyCredentials
            .AsNoTracking()
            .Include(item => item.MachineIdentity)
                .ThenInclude(identity => identity.Tenant)
            .Include(item => item.MachineIdentity)
                .ThenInclude(identity => identity.Permissions)
            .SingleOrDefaultAsync(item => item.KeyId == keyId, Context.RequestAborted);

        bool hashMatches = credentialRecord is not null
            && ApiKeySecret.Verify(secret, credentialRecord.SecretHash);

        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        if (!hashMatches
            || credentialRecord!.HashAlgorithm != ApiKeySecret.HashAlgorithm
            || credentialRecord.Status != ApiKeyStatus.Active
            || credentialRecord.ExpiresAt is { } expiresAt && expiresAt <= now
            || credentialRecord.MachineIdentity.Status != MachineIdentityStatus.Active
            || credentialRecord.MachineIdentity.Tenant.Status != TenantStatus.Active)
            return AuthenticateResult.Fail("Invalid API key.");

        var identity = credentialRecord.MachineIdentity;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"machine:{identity.MachineIdentityId}"),
            new(ClaimTypes.Name, identity.Name),
            new(CirrusClaimTypes.TenantId, identity.TenantId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(CirrusClaimTypes.Actor, $"apikey:machine:{identity.MachineIdentityId}"),
            new(CirrusClaimTypes.Provider, ApiKeyOptions.Scheme)
        };
        claims.AddRange(identity.Permissions.Select(permission =>
            new Claim(CirrusClaimTypes.Permission, permission.Permission.ToExternalName())));

        if (credentialRecord.LastUsedAt is null
            || now - credentialRecord.LastUsedAt >= Options.LastUsedUpdateInterval)
        {
            try
            {
                await dbContext.ApiKeyCredentials
                    .Where(item => item.ApiKeyCredentialId == credentialRecord.ApiKeyCredentialId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LastUsedAt, now), Context.RequestAborted);
            }
            catch (Exception exception) when (!Context.RequestAborted.IsCancellationRequested)
            {
                Logger.LogWarning(exception, "Updating API key last-used timestamp failed for key {KeyId}.", keyId);
            }
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = ApiKeyOptions.Scheme;
        return base.HandleChallengeAsync(properties);
    }
}
