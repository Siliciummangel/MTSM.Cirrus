using MTSM.Cirrus.Core.Exceptions;
using MTSM.Cirrus.Core.Providers.S3;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;
using MTSM.Cirrus.Worker.StorageV2;
using System.Security.Cryptography;

namespace MTSM.Cirrus.Core.Tests;

public sealed class StreamingPackWriterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AppendAndComplete_StreamsMultiplePartsWithStableOffsetsLengthsAndHash()
    {
        using var client = new RecordingS3Client();
        using S3ObjectStorage storage = client.CreateStorage();
        await using var writer = new StreamingPackWriter(storage, "bucket", "pack", default);
        byte[] first = new byte[7 * 1024 * 1024 + 19];
        byte[] second = new byte[6 * 1024 * 1024 + 31];
        new Random(7).NextBytes(first);
        new Random(8).NextBytes(second);

        PackEntry entry1 = await writer.AppendAsync(first, 123, default).WaitAsync(Timeout);
        PackEntry entry2 = await writer.AppendAsync(second, 456, default).WaitAsync(Timeout);
        UploadedPackContent uploaded = await writer.CompleteAsync(default).WaitAsync(Timeout);

        byte[] expected = [.. first, .. second];
        Assert.Equal(new PackEntry(0, first.Length, 123), entry1);
        Assert.Equal(new PackEntry(first.Length, second.Length, 456), entry2);
        Assert.Equal(expected.Length, uploaded.Length);
        Assert.Equal(expected, client.Parts.SelectMany(x => x).ToArray());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), uploaded.Sha256Hash);
        Assert.Equal("version-1", uploaded.Write.VersionId);
        Assert.Equal(new[] { 1, 2, 3 }, client.PartNumbers);
        Assert.Equal(5 * 1024 * 1024, client.MaximumPartSize);
        Assert.Equal(1, client.Completed);
        Assert.Equal(0, client.Aborted);
    }

    [Fact]
    public async Task SlowUpload_AppliesBackpressureAndCancellationUnblocksProducer()
    {
        var partStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var client = new RecordingS3Client
        {
            BeforePartAsync = async (_, token) =>
            {
                partStarted.TrySetResult();
                await Task.Delay(System.Threading.Timeout.Infinite, token);
            }
        };
        using S3ObjectStorage storage = client.CreateStorage();
        var writer = new StreamingPackWriter(storage, "bucket", "pack", cancellation.Token);
        try
        {
            Task append = writer.AppendAsync(new byte[12 * 1024 * 1024], 123, cancellation.Token);
            await partStarted.Task.WaitAsync(Timeout);
            Assert.False(append.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => append.WaitAsync(Timeout));
        }
        finally { await writer.DisposeAsync().AsTask().WaitAsync(Timeout); }
        Assert.Equal(0, client.Completed);
        Assert.Equal(1, client.Aborted);
        Assert.False(client.AbortTokenWasCancelled);
    }

    [Fact]
    public async Task FailedPart_UnblocksProducerAndAbortsUpload()
    {
        using var client = new RecordingS3Client
        {
            BeforePartAsync = (request, _) => request.PartNumber == 2
                ? Task.FromException(new IOException("Part upload failed.")) : Task.CompletedTask
        };
        using S3ObjectStorage storage = client.CreateStorage();
        await using (var writer = new StreamingPackWriter(storage, "bucket", "pack", default))
        {
            await Assert.ThrowsAsync<ObjectStorageException>(async () =>
            {
                await writer.AppendAsync(new byte[16 * 1024 * 1024], 123, default).WaitAsync(Timeout);
                await writer.CompleteAsync(default).WaitAsync(Timeout);
            });
        }
        Assert.Single(client.Parts);
        Assert.Equal(0, client.Completed);
        Assert.Equal(1, client.Aborted);
    }

    [Fact]
    public async Task ProducerFailure_DisposalAbortsInsteadOfCompletingPartialPack()
    {
        using var client = new RecordingS3Client();
        using S3ObjectStorage storage = client.CreateStorage();
        var writer = new StreamingPackWriter(storage, "bucket", "pack", default);
        await writer.AppendAsync(new byte[6 * 1024 * 1024], 123, default).WaitAsync(Timeout);
        // E.g. the next staging read, compression or compaction verification throws.
        await writer.DisposeAsync().AsTask().WaitAsync(Timeout);
        Assert.Single(client.Parts);
        Assert.Equal(0, client.Completed);
        Assert.Equal(1, client.Aborted);
    }

    [Fact]
    public async Task CompleteFailure_DoesNotReturnPackMetadataAndAborts()
    {
        using var client = new RecordingS3Client
        {
            BeforeCompleteAsync = _ => Task.FromException(new IOException("Complete failed."))
        };
        using S3ObjectStorage storage = client.CreateStorage();
        await using var writer = new StreamingPackWriter(storage, "bucket", "pack", default);
        await writer.AppendAsync(new byte[1024], 123, default);
        await Assert.ThrowsAsync<ObjectStorageException>(() => writer.CompleteAsync(default).WaitAsync(Timeout));
        Assert.Equal(0, client.Completed);
        Assert.Equal(1, client.Aborted);
    }

    [Fact]
    public async Task CancellationDuringComplete_AbortsUsingIndependentCancellationToken()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new RecordingS3Client
        {
            BeforeCompleteAsync = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        };
        using S3ObjectStorage storage = client.CreateStorage();
        await using (var writer = new StreamingPackWriter(storage, "bucket", "pack", cancellation.Token))
        {
            await writer.AppendAsync(new byte[1024], 123, cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.CompleteAsync(cancellation.Token).WaitAsync(Timeout));
        }
        Assert.Equal(0, client.Completed);
        Assert.Equal(1, client.Aborted);
        Assert.False(client.AbortTokenWasCancelled);
    }
}
