using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Config;
using MTSM.Cirrus.Core.Providers.S3;
using System.Net;

namespace MTSM.Cirrus.Core.Tests.TestInfrastructure;

internal static class S3TestConfiguration
{
    public const string ServiceUrlVariable =
        "CIRRUS_TEST_S3_SERVICE_URL";
    public const string AccessKeyVariable =
        "CIRRUS_TEST_S3_ACCESS_KEY";
    public const string SecretKeyVariable =
        "CIRRUS_TEST_S3_SECRET_KEY";
    public const string RegionVariable =
        "CIRRUS_TEST_S3_REGION";

    public static S3Options? TryGetOptions()
    {
        string? serviceUrl =
            Environment.GetEnvironmentVariable(ServiceUrlVariable);
        string? accessKey =
            Environment.GetEnvironmentVariable(AccessKeyVariable);
        string? secretKey =
            Environment.GetEnvironmentVariable(SecretKeyVariable);

        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(accessKey)
            || string.IsNullOrWhiteSpace(secretKey))
        {
            return null;
        }

        string? region =
            Environment.GetEnvironmentVariable(RegionVariable);

        return new S3Options
        {
            ServiceUrl = uri.ToString().TrimEnd('/'),
            AccessKey = accessKey,
            SecretKey = secretKey,
            Region = string.IsNullOrWhiteSpace(region)
                ? "us-east-1"
                : region.Trim(),
            ForcePathStyle = true,
            CreateBucketIfMissing = true
        };
    }
}

internal sealed class S3FactAttribute : FactAttribute
{
    public S3FactAttribute()
    {
        bool isContinuousIntegration = string.Equals(
            Environment.GetEnvironmentVariable("CI"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!isContinuousIntegration
            && S3TestConfiguration.TryGetOptions() is null)
        {
            Skip =
                $"Set {S3TestConfiguration.ServiceUrlVariable}, " +
                $"{S3TestConfiguration.AccessKeyVariable} and " +
                $"{S3TestConfiguration.SecretKeyVariable}.";
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class S3Collection : ICollectionFixture<S3Fixture>
{
    public const string Name = "S3-compatible object storage integration";
}

public sealed class S3Fixture : IAsyncLifetime
{
    private const string TestBucketPrefix = "cirrus-test-";

    private S3Options? Configuration { get; } =
        S3TestConfiguration.TryGetOptions();

    public string BucketName { get; } =
        $"{TestBucketPrefix}{Guid.NewGuid():N}";

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public S3ObjectStorage CreateStorage()
    {
        S3Options options = GetRequiredOptions();

        return new S3ObjectStorage(
            Options.Create(options),
            NullLogger<S3ObjectStorage>.Instance);
    }

    public string CreateObjectKey(string name)
    {
        return $"contract/{Guid.NewGuid():N}/{name}";
    }

    public async Task DisposeAsync()
    {
        if (Configuration is null)
        {
            return;
        }

        if (!BucketName.StartsWith(
                TestBucketPrefix,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Refusing to clean up a bucket outside the test namespace.");
        }

        using AmazonS3Client client = CreateClient(Configuration);

        try
        {
            string? continuationToken = null;

            do
            {
                ListObjectsV2Response response =
                    await client.ListObjectsV2Async(
                        new ListObjectsV2Request
                        {
                            BucketName = BucketName,
                            ContinuationToken = continuationToken
                        });

                foreach (S3Object item in response.S3Objects)
                {
                    await client.DeleteObjectAsync(
                        BucketName,
                        item.Key);
                }

                continuationToken = response.IsTruncated == true
                    ? response.NextContinuationToken
                    : null;
            }
            while (continuationToken is not null);

            await client.DeleteBucketAsync(BucketName);
        }
        catch (AmazonS3Exception exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // A test that fails before its first write creates no bucket.
        }
    }

    private S3Options GetRequiredOptions()
    {
        return Configuration
            ?? throw new InvalidOperationException(
                "The S3 integration fixture is not configured.");
    }

    private static AmazonS3Client CreateClient(S3Options options)
    {
        var credentials = new Amazon.Runtime.BasicAWSCredentials(
            options.AccessKey,
            options.SecretKey);

        return new AmazonS3Client(
            credentials,
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                ForcePathStyle = options.ForcePathStyle,
                AuthenticationRegion = options.Region
            });
    }
}
