using Microsoft.AspNetCore.Authentication;

namespace MTSM.Cirrus.API.Security;

public sealed class ApiKeyOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "ApiKey";
    public const string HashAlgorithm = "SHA-256";
    public TimeSpan LastUsedUpdateInterval { get; set; } = TimeSpan.FromMinutes(15);
}
