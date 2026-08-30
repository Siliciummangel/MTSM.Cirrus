using MTSM.Cirrus.Worker.StorageV2;
using System.Security.Cryptography;

namespace MTSM.Cirrus.Core.Tests;

public sealed class TemporaryPackBuilderTests
{
    [Fact]
    public async Task AppendAndSealAsync_ProducesStableOffsetsAndHash()
    {
        await using var builder = new TemporaryPackBuilder();
        PackEntry first = await builder.AppendAsync("first"u8.ToArray(), 5, default);
        PackEntry second = await builder.AppendAsync("second"u8.ToArray(), 6, default);
        SealedPack pack = await builder.SealAsync(default);
        using var output = new MemoryStream();
        await pack.Content.CopyToAsync(output);
        byte[] bytes = output.ToArray();

        Assert.Equal(0, first.Offset);
        Assert.Equal(5, second.Offset);
        Assert.Equal("firstsecond"u8.ToArray(), bytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), pack.Sha256Hash);
    }
}
