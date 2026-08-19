using System.Security.Cryptography;
using System.Text;

namespace MTSM.Cirrus.Core.Security;

public static class ApiKeySecret
{
    public const string Prefix = "cirrus_";
    public const string HashAlgorithm = "SHA-256";

    public static GeneratedApiKey Generate()
    {
        string keyId = Base64Url(RandomNumberGenerator.GetBytes(12));
        string secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new GeneratedApiKey(keyId, $"{Prefix}{keyId}.{secret}", Hash(secret));
    }

    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    public static bool Verify(ReadOnlySpan<char> secret, ReadOnlySpan<byte> expectedHash)
    {
        if (secret.Length != 43)
        {
            return false;
        }

        Span<byte> secretBytes = stackalloc byte[43];

        if (!Encoding.ASCII.TryGetBytes(
                secret,
                secretBytes,
                out int bytesWritten)
            || bytesWritten != secretBytes.Length)
        {
            return false;
        }

        Span<byte> actualHash = stackalloc byte[SHA256.HashSizeInBytes];

        SHA256.HashData(secretBytes, actualHash);

        Span<byte> dummyHash = stackalloc byte[SHA256.HashSizeInBytes];

        dummyHash.Clear();

        bool hasValidHashLength = expectedHash.Length == SHA256.HashSizeInBytes;

        ReadOnlySpan<byte> hashToCompare =
            hasValidHashLength
                ? expectedHash
                : dummyHash;

        bool hashesMatch =
            CryptographicOperations.FixedTimeEquals(
                actualHash,
                hashToCompare);

        CryptographicOperations.ZeroMemory(secretBytes);
        CryptographicOperations.ZeroMemory(actualHash);

        return hasValidHashLength & hashesMatch;
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        System.Buffers.Text.Base64Url.EncodeToString(bytes);
}

public sealed record GeneratedApiKey(string KeyId, string Value, byte[] SecretHash);
