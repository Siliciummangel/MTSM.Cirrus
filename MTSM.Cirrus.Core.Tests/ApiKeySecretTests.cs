using MTSM.Cirrus.Core.Security;

namespace MTSM.Cirrus.Core.Tests;

public sealed class ApiKeySecretTests
{
    [Fact]
    public void Generate_CreatesUniqueOpaqueKeysWhoseSecretsVerify()
    {
        GeneratedApiKey first = ApiKeySecret.Generate();
        GeneratedApiKey second = ApiKeySecret.Generate();

        Assert.StartsWith("cirrus_", first.Value);
        Assert.NotEqual(first.KeyId, second.KeyId);
        Assert.NotEqual(first.Value, second.Value);

        string secret = first.Value[(first.Value.IndexOf('.') + 1)..];
        Assert.True(ApiKeySecret.Verify(secret, first.SecretHash));
        Assert.False(ApiKeySecret.Verify("wrong-secret", first.SecretHash));
        Assert.False(ApiKeySecret.Verify(secret, first.SecretHash.AsSpan(0, 16)));
        Assert.DoesNotContain(secret, Convert.ToHexString(first.SecretHash), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('+', first.Value);
        Assert.DoesNotContain('/', first.Value);
        Assert.DoesNotContain('=', first.Value);
    }
}
