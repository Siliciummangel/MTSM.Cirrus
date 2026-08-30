using ZstdSharp;

namespace MTSM.Cirrus.Core.Tests;

public sealed class ZstdCompressionTests
{
    [Fact]
    public void ZstdFrame_RoundTripsAndActuallyCompressesRepetitiveChunk()
    {
        byte[] source = Enumerable.Repeat((byte)0x5a, 256 * 1024).ToArray();
        byte[] compressed;
        using (var compressor = new Compressor(3))
            compressed = compressor.Wrap(source).ToArray();
        using var decompressor = new Decompressor();
        byte[] restored = decompressor.Unwrap(compressed, source.Length).ToArray();
        Assert.True(compressed.Length < source.Length);
        Assert.Equal(source, restored);
    }
}
