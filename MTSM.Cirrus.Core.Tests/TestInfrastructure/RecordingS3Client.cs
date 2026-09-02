using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Config;
using MTSM.Cirrus.Core.Providers.S3;

namespace MTSM.Cirrus.Core.Tests.TestInfrastructure;

internal sealed class RecordingS3Client() : AmazonS3Client(new AnonymousAWSCredentials(),
    new AmazonS3Config { ServiceURL = "http://127.0.0.1:1" })
{
    public List<byte[]> Parts { get; } = [];
    public List<int> PartNumbers { get; } = [];
    public Func<UploadPartRequest, CancellationToken, Task>? BeforePartAsync { get; set; }
    public Func<CancellationToken, Task>? BeforeCompleteAsync { get; set; }
    public int Initiated { get; private set; }
    public int Completed { get; private set; }
    public int Aborted { get; private set; }
    public bool AbortTokenWasCancelled { get; private set; }
    public long MaximumPartSize { get; private set; }

    public S3ObjectStorage CreateStorage() => new(Options.Create(new S3Options
    {
        ServiceUrl = "http://127.0.0.1:1", CreateBucketIfMissing = false,
        AccessKey = "test", SecretKey = "test"
    }), NullLogger<S3ObjectStorage>.Instance, this);

    public override Task<InitiateMultipartUploadResponse> InitiateMultipartUploadAsync(
        InitiateMultipartUploadRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Initiated++;
        return Task.FromResult(new InitiateMultipartUploadResponse { UploadId = $"upload-{Initiated}" });
    }

    public override async Task<UploadPartResponse> UploadPartAsync(
        UploadPartRequest request, CancellationToken cancellationToken = default)
    {
        if (BeforePartAsync is not null) await BeforePartAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var bytes = new MemoryStream();
        await request.InputStream.CopyToAsync(bytes, cancellationToken);
        Assert.Equal(request.PartSize, bytes.Length);
        MaximumPartSize = Math.Max(MaximumPartSize, bytes.Length);
        Parts.Add(bytes.ToArray());
        PartNumbers.Add(request.PartNumber!.Value);
        return new UploadPartResponse { ETag = $"part-{request.PartNumber}" };
    }

    public override async Task<CompleteMultipartUploadResponse> CompleteMultipartUploadAsync(
        CompleteMultipartUploadRequest request, CancellationToken cancellationToken = default)
    {
        if (BeforeCompleteAsync is not null) await BeforeCompleteAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(Enumerable.Range(1, request.PartETags.Count), request.PartETags.Select(x => x.PartNumber!.Value));
        Completed++;
        return new CompleteMultipartUploadResponse { VersionId = "version-1", ETag = "pack-etag" };
    }

    public override Task<AbortMultipartUploadResponse> AbortMultipartUploadAsync(
        AbortMultipartUploadRequest request, CancellationToken cancellationToken = default)
    {
        Aborted++;
        AbortTokenWasCancelled = cancellationToken.IsCancellationRequested;
        return Task.FromResult(new AbortMultipartUploadResponse());
    }
}
