using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Config;
using MTSM.Cirrus.Core.Exceptions;
using MTSM.Cirrus.Core.Models;
using System.Net;

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
        string? encryptionKeyId = null,
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

        string? normalizedContentType = string.IsNullOrWhiteSpace(contentType)
            ? null
            : contentType.Trim();

        if (normalizedContentType?.Length > 255
            || normalizedContentType?.Any(char.IsControl) == true)
        {
            throw new ArgumentException(
                "The content type is invalid.",
                nameof(contentType));
        }

        await EnsureBucketExistsAsync(bucketName, cancellationToken);

        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = content,
            AutoCloseStream = false,
            UseChunkEncoding = _options.UseChunkEncoding,
            DisableDefaultChecksumValidation =
                _options.DisableDefaultChecksumValidation,
            ContentType = normalizedContentType is null
                ? "application/octet-stream"
                : normalizedContentType
        };

        if (!string.IsNullOrWhiteSpace(encryptionKeyId))
        {
            request.ServerSideEncryptionMethod =
                ServerSideEncryptionMethod.AWSKMS;
            request.ServerSideEncryptionKeyManagementServiceKeyId =
                encryptionKeyId.Trim();
        }

        try
        {
            PutObjectResponse response = await _s3Client.PutObjectAsync(
                request,
                cancellationToken);

            _logger.LogInformation(
                "Stored an object in S3 bucket {BucketName}.",
                bucketName);

            _logger.LogDebug(
                "Stored S3 object {BucketName}/{ObjectKey} with ETag {ETag} " +
                "and version {VersionId}.",
                bucketName,
                objectKey,
                NormalizeETag(response.ETag),
                NormalizeHeaderValue(response.VersionId));

            return new ObjectStorageWriteResult(
                NormalizeHeaderValue(response.VersionId),
                NormalizeETag(response.ETag));
        }
        catch (AmazonS3Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            LogS3Failure("write", exception);

            throw CreateStorageException(
                "write",
                exception);
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            LogUnexpectedStorageFailure("write", exception);
            throw CreateStorageException("write", exception);
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

            if (response.ResponseStream is null)
            {
                response.Dispose();

                throw new ObjectStorageException(
                    "Object storage returned no response stream.");
            }

            // The caller owns and disposes this stream. The AWS response stream
            // keeps the underlying HTTP response alive until it is disposed.
            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            LogS3Failure("read", exception);

            throw CreateStorageException(
                "read",
                exception);
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            LogUnexpectedStorageFailure("read", exception);
            throw CreateStorageException("read", exception);
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
            LogS3Failure("check existence", exception);

            throw CreateStorageException(
                "check existence of",
                exception);
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            LogUnexpectedStorageFailure("check existence", exception);
            throw CreateStorageException("check existence of", exception);
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
                catch (AmazonS3Exception exception)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    LogS3Failure("create bucket", exception);

                    throw new ObjectStorageException(
                        "Creating the object-storage bucket failed.",
                        exception);
                }
                catch (Exception exception)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    LogUnexpectedStorageFailure("create bucket", exception);

                    throw new ObjectStorageException(
                        "Creating the object-storage bucket failed.",
                        exception);
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
            LogS3Failure("check bucket", exception);

            throw new ObjectStorageException(
                "Checking the object-storage bucket failed.",
                exception);
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            LogUnexpectedStorageFailure("check bucket", exception);

            throw new ObjectStorageException(
                "Checking the object-storage bucket failed.",
                exception);
        }
    }

    public async Task<ObjectStorageDeleteOutcome> DeleteAsync(
        string bucketName,
        string objectKey,
        string? versionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateLocation(bucketName, objectKey);

        string? normalizedVersionId = string.IsNullOrWhiteSpace(versionId)
            ? null
            : versionId.Trim();

        try
        {
            await _s3Client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = bucketName,
                    Key = objectKey,
                    VersionId = normalizedVersionId
                },
                cancellationToken);

            await _s3Client.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey,
                    VersionId = normalizedVersionId
                },
                cancellationToken);

            _logger.LogInformation(
                "Deleted an object from S3 bucket {BucketName}.",
                bucketName);

            return ObjectStorageDeleteOutcome.Deleted;
        }
        catch (AmazonS3Exception exception)
            when (IsNotFound(exception))
        {
            return ObjectStorageDeleteOutcome.NotFound;
        }
        catch (AmazonS3Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            LogS3Failure("delete", exception);
            throw CreateStorageException("delete", exception);
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            LogUnexpectedStorageFailure("delete", exception);
            throw CreateStorageException("delete", exception);
        }
    }

    private static ObjectStorageException CreateStorageException(
        string operation,
        Exception exception)
    {
        return new ObjectStorageException(
            $"The object-storage operation '{operation}' failed.",
            exception);
    }

    private void LogS3Failure(
        string operation,
        AmazonS3Exception exception)
    {
        _logger.LogError(
            "S3 operation {StorageOperation} failed with HTTP status {StatusCode}, " +
            "error code {ErrorCode} and request ID {RequestId}.",
            operation,
            (int)exception.StatusCode,
            exception.ErrorCode,
            exception.RequestId);
    }

    private void LogUnexpectedStorageFailure(
        string operation,
        Exception exception)
    {
        _logger.LogError(
            "Object-storage operation {StorageOperation} failed with error type " +
            "{StorageErrorType}.",
            operation,
            exception.GetType().Name);
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
        return string.Equals(
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

        if (!string.Equals(bucketName, bucketName.Trim(), StringComparison.Ordinal)
            || bucketName.Length > 255
            || bucketName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The bucket name is invalid.",
                nameof(bucketName));
        }

        if (objectKey.Length > 1024 || objectKey.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The object key is invalid.",
                nameof(objectKey));
        }
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
