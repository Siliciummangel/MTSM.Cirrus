using MTSM.Cirrus.Core.Streams;
using System.Security.Cryptography;

namespace MTSM.Cirrus.Core.Tests;

public sealed class HashingReadStreamTests
{
    [Fact]
    public async Task ReadAsync_CalculatesHashAndByteCount()
    {
        byte[] content = "production archive content"u8.ToArray();
        await using var source = new MemoryStream(content);
        await using var subject = new HashingReadStream(source, leaveOpen: true);
        await using var destination = new MemoryStream();

        await subject.CopyToAsync(destination);

        string expectedHash = Convert.ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();

        Assert.Equal(content, destination.ToArray());
        Assert.Equal(content.Length, subject.BytesRead);
        Assert.Equal(expectedHash, subject.GetHashHex());
        Assert.Equal(expectedHash, subject.GetHashHex());
    }

    [Fact]
    public async Task DisposeAsync_WithLeaveOpen_DoesNotDisposeSource()
    {
        var source = new MemoryStream("content"u8.ToArray());
        var subject = new HashingReadStream(source, leaveOpen: true);

        await subject.DisposeAsync();

        Assert.True(source.CanRead);
        await source.DisposeAsync();
    }

    [Fact]
    public void ReadingAfterHashFinalization_IsRejected()
    {
        using var source = new MemoryStream("content"u8.ToArray());
        using var subject = new HashingReadStream(source);

        _ = subject.GetHashHex();

        Assert.Throws<InvalidOperationException>(() => subject.ReadByte());
    }
}
