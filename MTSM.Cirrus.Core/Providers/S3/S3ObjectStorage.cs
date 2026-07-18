using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Config;
using MTSM.Cirrus.Core.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace MTSM.Cirrus.Core.Providers.S3;

public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly S3Options _options;
    private readonly ILogger<S3ObjectStorage> _logger;
    private readonly SemaphoreSlim _bucketInitializationLock = new(1, 1);
    private readonly HashSet<string> _initializedBuckets = new(StringComparer.Ordinal);
    private bool _disposed;

    public S3ObjectStorage(
        IOptions<S3Options> options,
        ILogger<S3ObjectStorage> logger)
    {
        _options = options.Value;
        _logger = logger;

        var credentials = new Amazon.Runtime.BasicAWSCredentials(
            _options.AccessKey,
            _options.SecretKey);

        var configuration = new AmazonS3Config
        {
            ServiceURL = _options.ServiceUrl.TrimEnd('/'),
            ForcePathStyle = _options.ForcePathStyle,
            AuthenticationRegion = _options.Region
        };

        _s3Client = new AmazonS3Client(credentials, configuration);
    }

    public async Task<ObjectStorageWriteResult> WriteAsync(
        string bucketName,
        string objectKey,
        Stream content,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateLocation(bucketName, objectKey);
        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanRead)
        {
            throw new ArgumentException(
                "The content stream must be readable.",
                nameof(content));
        }

        await EnsureBucketExistsAsync(bucketName, cancellationToken);

        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType
        };

        try
        {
            PutObjectResponse response = await _s3Client.PutObjectAsync(
                request,
                cancellationToken);

            _logger.LogInformation(
                "Stored S3 object {BucketName}/{ObjectKey} with ETag {ETag} and version {VersionId}.",
                bucketName,
                objectKey,
                response.ETag,
                response.VersionId);

            return new ObjectStorageWriteResult(
                NormalizeHeaderValue(response.VersionId),
                NormalizeETag(response.ETag));
        }
        catch (AmazonS3Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateStorageException(
                "write",
                bucketName,
                objectKey,
                exception);
        }
    }

    public async Task<Stream> OpenReadAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateLocation(bucketName, objectKey);

        try
        {
            GetObjectResponse response = await _s3Client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey
                },
                cancellationToken);

            // The caller owns and disposes this stream. The AWS response stream
            // keeps the underlying HTTP response alive until it is disposed.
            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateStorageException(
                "read",
                bucketName,
                objectKey,
                exception);
        }
    }

    public async Task<bool> ExistsAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateLocation(bucketName, objectKey);

        try
        {
            await _s3Client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = objectKey
                },
                cancellationToken);

            return true;
        }
        catch (AmazonS3Exception exception)
            when (IsNotFound(exception))
        {
            return false;
        }
        catch (AmazonS3Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateStorageException(
                "check existence of",
                bucketName,
                objectKey,
                exception);
        }
    }

    private async Task EnsureBucketExistsAsync(
        string bucketName,
        CancellationToken cancellationToken)
    {
        if (!_options.CreateBucketIfMissing)
        {
            return;
        }

        lock (_initializedBuckets)
        {
            if (_initializedBuckets.Contains(bucketName))
            {
                return;
            }
        }

        await _bucketInitializationLock.WaitAsync(cancellationToken);

        try
        {
            lock (_initializedBuckets)
            {
                if (_initializedBuckets.Contains(bucketName))
                {
                    return;
                }
            }

            bool exists = await BucketExistsAsync(
                bucketName,
                cancellationToken);

            if (!exists)
            {
                try
                {
                    await _s3Client.PutBucketAsync(
                        new PutBucketRequest
                        {
                            BucketName = bucketName,
                            UseClientRegion = true
                        },
                        cancellationToken);

                    _logger.LogInformation(
                        "Created S3 bucket {BucketName}.",
                        bucketName);
                }
                catch (AmazonS3Exception exception)
                    when (IsBucketAlreadyExists(exception))
                {
                    // Another instance may have created it concurrently.
                }
            }

            lock (_initializedBuckets)
            {
                _initializedBuckets.Add(bucketName);
            }
        }
        finally
        {
            _bucketInitializationLock.Release();
        }
    }

    private async Task<bool> BucketExistsAsync(
        string bucketName,
        CancellationToken cancellationToken)
    {
        try
        {
            await _s3Client.GetBucketAclAsync(
                new GetBucketAclRequest
                {
                    BucketName = bucketName
                },
                cancellationToken);

            return true;
        }
        catch (AmazonS3Exception exception)
            when (IsNotFound(exception))
        {
            return false;
        }
        catch (AmazonS3Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"Checking S3 bucket '{bucketName}' failed " +
                $"with status {(int)exception.StatusCode} ({exception.StatusCode}) " +
                $"and error code '{exception.ErrorCode}'.",
                exception);
        }
    }

    private static InvalidOperationException CreateStorageException(
        string operation,
        string bucketName,
        string objectKey,
        AmazonS3Exception exception)
    {
        return new InvalidOperationException(
            $"Failed to {operation} S3 object " +
            $"'{bucketName}/{objectKey}'. " +
            $"Status: {(int)exception.StatusCode} ({exception.StatusCode}), " +
            $"error code: '{exception.ErrorCode}'.",
            exception);
    }

    private static bool IsNotFound(AmazonS3Exception exception)
    {
        return exception.StatusCode == HttpStatusCode.NotFound ||
               string.Equals(
                   exception.ErrorCode,
                   "NoSuchKey",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   exception.ErrorCode,
                   "NoSuchBucket",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBucketAlreadyExists(AmazonS3Exception exception)
    {
        return exception.StatusCode == HttpStatusCode.Conflict ||
               string.Equals(
                   exception.ErrorCode,
                   "BucketAlreadyExists",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   exception.ErrorCode,
                   "BucketAlreadyOwnedByYou",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeHeaderValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? NormalizeETag(string? eTag)
    {
        return string.IsNullOrWhiteSpace(eTag)
            ? null
            : eTag.Trim().Trim('"');
    }

    private static void ValidateLocation(
        string bucketName,
        string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bucketInitializationLock.Dispose();
        _s3Client.Dispose();
    }
}
