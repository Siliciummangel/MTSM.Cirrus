using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Config;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Exceptions;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Core.Streams;
using System.Security.Cryptography;
using System.Text.Json;

namespace MTSM.Cirrus.Core.Services;

public sealed class ArchiveService : IArchiveService
{
    private const int MaximumPageSize = 500;

    private readonly CirrusDbContext _dbContext;
    private readonly IObjectStorage _objectStorage;
    private readonly ArchiveOptions _options;
    private readonly ILogger<ArchiveService> _logger;

    public ArchiveService(
        CirrusDbContext dbContext,
        IObjectStorage objectStorage,
        IOptions<ArchiveOptions> options,
        ILogger<ArchiveService> logger)
    {
        _dbContext = dbContext;
        _objectStorage = objectStorage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ArchiveFileResult> ArchiveAsync(
        ArchiveFileRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveRequest(request);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        DateOnly retentionUntil = await ResolveRetentionUntilAsync(
            request,
            cancellationToken);

        string objectKey = CreateObjectKey(
            request,
            now);

        var archiveObject = new ArchiveObject
        {
            ObjectKey = objectKey,
            BucketName = _options.BucketName,

            FileType = request.FileType,
            MimeType = request.MimeType,
            SourceSystem = request.SourceSystem,
            Partner = request.Partner,
            OriginalFilename = request.OriginalFilename,

            SizeBytes = 0,

            ReceivedAt = request.ReceivedAt,
            ArchivedAt = null,

            RetentionPolicyId = request.RetentionPolicyId,
            RetentionUntil = retentionUntil,

            ArchiveStatus = ArchiveStatus.Pending,

            IsWormProtected = false,
            CreatedBy = request.CreatedBy
        };

        AddBusinessReferences(
            archiveObject,
            request.BusinessReferences,
            now);

        archiveObject.Events.Add(new ArchiveEvent
        {
            EventType = ArchiveEventType.Created,
            EventTimestamp = now,
            Actor = request.CreatedBy
        });

        _dbContext.ArchiveObjects.Add(archiveObject);

        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await using var hashingStream = new HashingReadStream(
                request.Content,
                leaveOpen: true);

            ObjectStorageWriteResult storageResult =
                await _objectStorage.WriteAsync(
                    archiveObject.BucketName,
                    archiveObject.ObjectKey,
                    hashingStream,
                    archiveObject.MimeType,
                    cancellationToken);

            string sha256Hash = hashingStream.GetHashHex();
            long sizeBytes = hashingStream.BytesRead;
            DateTimeOffset archivedAt = DateTimeOffset.UtcNow;

            archiveObject.Sha256Hash = sha256Hash;
            archiveObject.SizeBytes = sizeBytes;
            archiveObject.ArchivedAt = archivedAt;
            archiveObject.ArchiveStatus = ArchiveStatus.Active;

            archiveObject.StorageVersionId =
                storageResult.VersionId ?? storageResult.ETag;

            archiveObject.Events.Add(new ArchiveEvent
            {
                EventType = ArchiveEventType.Archived,
                EventTimestamp = archivedAt,
                Actor = request.CreatedBy
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Archive object {ArchiveObjectId} was stored as {ObjectKey}.",
                archiveObject.ArchiveObjectId,
                archiveObject.ObjectKey);

            return new ArchiveFileResult(
                archiveObject.ArchiveObjectId,
                archiveObject.ObjectKey,
                archiveObject.Sha256Hash,
                archiveObject.SizeBytes,
                archivedAt);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            await MarkAsErrorBestEffortAsync(
                archiveObject,
                "UPLOAD_CANCELLED",
                "The archive operation was cancelled.",
                request.CreatedBy);

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Archiving object {ArchiveObjectId} failed.",
                archiveObject.ArchiveObjectId);

            await MarkAsErrorBestEffortAsync(
                archiveObject,
                "ARCHIVE_FAILED",
                exception.Message,
                request.CreatedBy);

            throw new ArchiveException(
                $"Archiving object {archiveObject.ArchiveObjectId} failed.",
                exception);
        }
    }

    public async Task<ArchiveDownloadResult> DownloadAsync(
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveObjectId(archiveObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        ArchiveObject archiveObject =
            await GetActiveArchiveObjectAsync(
                archiveObjectId,
                cancellationToken);

        Stream content;

        try
        {
            content = await _objectStorage.OpenReadAsync(
                archiveObject.BucketName,
                archiveObject.ObjectKey,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Opening archive object {ArchiveObjectId} from object storage failed.",
                archiveObjectId);

            throw new ArchiveException(
                $"Opening archive object {archiveObjectId} failed.",
                exception);
        }

        if (!content.CanRead)
        {
            await content.DisposeAsync();

            throw new ArchiveException(
                $"The storage stream for archive object " +
                $"{archiveObjectId} is not readable.");
        }

        archiveObject.Events.Add(new ArchiveEvent
        {
            EventType = ArchiveEventType.Downloaded,
            EventTimestamp = DateTimeOffset.UtcNow,
            Actor = actor
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }

        _logger.LogInformation(
            "Archive object {ArchiveObjectId} was opened for download by {Actor}.",
            archiveObjectId,
            actor);

        return new ArchiveDownloadResult(
            archiveObject.ArchiveObjectId,
            archiveObject.OriginalFilename,
            archiveObject.MimeType,
            archiveObject.SizeBytes,
            archiveObject.Sha256Hash!,
            content);
    }

    public async Task<ArchiveMetadataResult?> GetMetadataAsync(
        long archiveObjectId,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveObjectId(archiveObjectId);

        return await _dbContext.ArchiveObjects
            .AsNoTracking()
            .Where(x =>
                x.ArchiveObjectId == archiveObjectId)
            .Select(x => new ArchiveMetadataResult(
                x.ArchiveObjectId,
                x.ObjectKey,
                x.BucketName,
                x.FileType,
                x.MimeType,
                x.SourceSystem,
                x.Partner,
                x.OriginalFilename,
                x.Sha256Hash,
                x.SizeBytes,
                x.ReceivedAt,
                x.ArchivedAt,
                x.RetentionUntil,
                x.RetentionPolicyId,
                x.ArchiveStatus,
                x.StorageVersionId,
                x.EncryptionKeyId,
                x.IsWormProtected,
                x.CreatedBy,

                x.BusinessReferences
                    .OrderBy(reference =>
                        reference.BusinessReferenceTypeId)
                    .ThenBy(reference =>
                        reference.ReferenceValue)
                    .Select(reference =>
                        new ArchiveBusinessReferenceResult(
                            reference.BusinessReferenceTypeId,
                            reference.ReferenceValue,
                            reference.BusinessType,
                            reference.Tenant,
                            reference.CreatedAt))
                    .ToArray(),

                x.Events
                    .OrderBy(archiveEvent =>
                        archiveEvent.EventTimestamp)
                    .Select(archiveEvent =>
                        new ArchiveEventResult(
                            archiveEvent.ArchiveEventId,
                            archiveEvent.EventType,
                            archiveEvent.EventTimestamp,
                            archiveEvent.Actor,
                            archiveEvent.DetailsJson))
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ArchiveSearchResult> SearchAsync(
        ArchiveSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateSearchRequest(request);

        IQueryable<ArchiveObject> query =
            _dbContext.ArchiveObjects
                .AsNoTracking();

        query = ApplySearchFilters(
            query,
            request);

        long totalCount =
            await query.LongCountAsync(cancellationToken);

        int totalPages = totalCount == 0
            ? 0
            : checked((int)Math.Ceiling(
                totalCount / (double)request.PageSize));

        int skip = checked(
            (request.PageNumber - 1) * request.PageSize);

        ArchiveSearchItem[] items =
            await query
                .OrderByDescending(x => x.ReceivedAt)
                .ThenByDescending(x => x.ArchiveObjectId)
                .Skip(skip)
                .Take(request.PageSize)
                .Select(x => new ArchiveSearchItem(
                    x.ArchiveObjectId,
                    x.FileType,
                    x.MimeType,
                    x.SourceSystem,
                    x.Partner,
                    x.OriginalFilename,
                    x.Sha256Hash,
                    x.SizeBytes,
                    x.ReceivedAt,
                    x.ArchivedAt,
                    x.RetentionUntil,
                    x.ArchiveStatus,

                    x.BusinessReferences
                        .OrderBy(reference =>
                            reference.BusinessReferenceTypeId)
                        .ThenBy(reference =>
                            reference.ReferenceValue)
                        .Select(reference =>
                            new ArchiveBusinessReferenceResult(
                                reference.BusinessReferenceTypeId,
                                reference.ReferenceValue,
                                reference.BusinessType,
                                reference.Tenant,
                                reference.CreatedAt))
                        .ToArray()))
                .ToArrayAsync(cancellationToken);

        return new ArchiveSearchResult(
            items,
            request.PageNumber,
            request.PageSize,
            totalCount,
            totalPages);
    }

    public async Task<ArchiveIntegrityResult> VerifyIntegrityAsync(
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveObjectId(archiveObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        ArchiveObject archiveObject =
            await GetActiveArchiveObjectAsync(
                archiveObjectId,
                cancellationToken);

        await using Stream content =
            await _objectStorage.OpenReadAsync(
                archiveObject.BucketName,
                archiveObject.ObjectKey,
                cancellationToken);

        if (!content.CanRead)
        {
            throw new ArchiveException(
                $"The storage stream for archive object " +
                $"{archiveObjectId} is not readable.");
        }

        var buffer = new byte[128 * 1024];

        using IncrementalHash hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

        long actualSizeBytes = 0;

        while (true)
        {
            int bytesRead = await content.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            hash.AppendData(
                buffer,
                0,
                bytesRead);

            actualSizeBytes += bytesRead;
        }

        string actualSha256Hash =
            Convert.ToHexString(hash.GetHashAndReset())
                .ToLowerInvariant();

        string expectedSha256Hash =
            archiveObject.Sha256Hash!;

        bool hashMatches =
            string.Equals(
                expectedSha256Hash,
                actualSha256Hash,
                StringComparison.OrdinalIgnoreCase);

        bool sizeMatches =
            archiveObject.SizeBytes == actualSizeBytes;

        bool isValid =
            hashMatches && sizeMatches;

        DateTimeOffset verifiedAt =
            DateTimeOffset.UtcNow;

        archiveObject.Events.Add(new ArchiveEvent
        {
            EventType = isValid
                ? ArchiveEventType.IntegrityVerified
                : ArchiveEventType.IntegrityCheckFailed,

            EventTimestamp = verifiedAt,
            Actor = actor,

            DetailsJson = CreateIntegrityDetails(
                expectedSha256Hash,
                actualSha256Hash,
                archiveObject.SizeBytes,
                actualSizeBytes,
                hashMatches,
                sizeMatches)
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (isValid)
        {
            _logger.LogInformation(
                "Integrity of archive object {ArchiveObjectId} was verified.",
                archiveObjectId);
        }
        else
        {
            _logger.LogError(
                "Integrity verification of archive object {ArchiveObjectId} failed. " +
                "Expected hash {ExpectedHash}, actual hash {ActualHash}, " +
                "expected size {ExpectedSize}, actual size {ActualSize}.",
                archiveObjectId,
                expectedSha256Hash,
                actualSha256Hash,
                archiveObject.SizeBytes,
                actualSizeBytes);
        }

        return new ArchiveIntegrityResult(
            archiveObject.ArchiveObjectId,
            isValid,
            expectedSha256Hash,
            actualSha256Hash,
            archiveObject.SizeBytes,
            actualSizeBytes,
            verifiedAt);
    }

    private async Task<ArchiveObject> GetActiveArchiveObjectAsync(
        long archiveObjectId,
        CancellationToken cancellationToken)
    {
        ArchiveObject? archiveObject =
            await _dbContext.ArchiveObjects
                .SingleOrDefaultAsync(
                    x => x.ArchiveObjectId == archiveObjectId,
                    cancellationToken);

        if (archiveObject is null)
        {
            throw new ArchiveObjectNotFoundException(
                archiveObjectId);
        }

        if (archiveObject.ArchiveStatus != ArchiveStatus.Active)
        {
            throw new ArchiveObjectUnavailableException(
                archiveObjectId,
                archiveObject.ArchiveStatus);
        }

        if (string.IsNullOrWhiteSpace(
                archiveObject.Sha256Hash))
        {
            throw new ArchiveException(
                $"Archive object {archiveObjectId} has no SHA-256 hash.");
        }

        return archiveObject;
    }

    private static IQueryable<ArchiveObject> ApplySearchFilters(
        IQueryable<ArchiveObject> query,
        ArchiveSearchRequest request)
    {
        if (request.ArchiveObjectId is not null)
        {
            query = query.Where(x =>
                x.ArchiveObjectId ==
                request.ArchiveObjectId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Tenant))
        {
            string tenant = request.Tenant.Trim();

            query = query.Where(x =>
                x.BusinessReferences.Any(reference =>
                    reference.Tenant == tenant));
        }

        if (!string.IsNullOrWhiteSpace(request.FileType))
        {
            string fileType = request.FileType.Trim();

            query = query.Where(x =>
                x.FileType == fileType);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceSystem))
        {
            string sourceSystem =
                request.SourceSystem.Trim();

            query = query.Where(x =>
                x.SourceSystem == sourceSystem);
        }

        if (!string.IsNullOrWhiteSpace(request.Partner))
        {
            string partner =
                request.Partner.Trim();

            query = query.Where(x =>
                x.Partner == partner);
        }

        if (!string.IsNullOrWhiteSpace(
                request.OriginalFilename))
        {
            string originalFilename =
                request.OriginalFilename.Trim();

            query = query.Where(x =>
                EF.Functions.ILike(
                    x.OriginalFilename,
                    $"%{originalFilename}%"));
        }

        if (!string.IsNullOrWhiteSpace(
                request.Sha256Hash))
        {
            string sha256Hash =
                request.Sha256Hash
                    .Trim()
                    .ToLowerInvariant();

            query = query.Where(x =>
                x.Sha256Hash == sha256Hash);
        }

        if (request.ArchiveStatus is not null)
        {
            query = query.Where(x =>
                x.ArchiveStatus ==
                request.ArchiveStatus.Value);
        }

        if (request.ReceivedFrom is not null)
        {
            query = query.Where(x =>
                x.ReceivedAt >=
                request.ReceivedFrom.Value);
        }

        if (request.ReceivedUntil is not null)
        {
            query = query.Where(x =>
                x.ReceivedAt <=
                request.ReceivedUntil.Value);
        }

        if (request.ArchivedFrom is not null)
        {
            query = query.Where(x =>
                x.ArchivedAt != null &&
                x.ArchivedAt >=
                request.ArchivedFrom.Value);
        }

        if (request.ArchivedUntil is not null)
        {
            query = query.Where(x =>
                x.ArchivedAt != null &&
                x.ArchivedAt <=
                request.ArchivedUntil.Value);
        }

        if (request.BusinessReferenceTypeId is not null)
        {
            int referenceTypeId =
                request.BusinessReferenceTypeId.Value;

            query = query.Where(x =>
                x.BusinessReferences.Any(reference =>
                    reference.BusinessReferenceTypeId ==
                    referenceTypeId));
        }

        if (!string.IsNullOrWhiteSpace(
                request.BusinessReferenceValue))
        {
            string referenceValue =
                request.BusinessReferenceValue.Trim();

            query = query.Where(x =>
                x.BusinessReferences.Any(reference =>
                    reference.ReferenceValue ==
                    referenceValue));
        }

        if (!string.IsNullOrWhiteSpace(
                request.BusinessType))
        {
            string businessType =
                request.BusinessType.Trim();

            query = query.Where(x =>
                x.BusinessReferences.Any(reference =>
                    reference.BusinessType ==
                    businessType));
        }

        return query;
    }

    private async Task<DateOnly> ResolveRetentionUntilAsync(
        ArchiveFileRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RetentionUntil is not null)
        {
            return request.RetentionUntil.Value;
        }

        if (request.RetentionPolicyId is null)
        {
            throw new ArgumentException(
                "Either RetentionUntil or RetentionPolicyId must be supplied.",
                nameof(request));
        }

        RetentionPolicy? policy =
            await _dbContext.RetentionPolicies
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.RetentionPolicyId ==
                         request.RetentionPolicyId.Value,
                    cancellationToken);

        if (policy is null)
        {
            throw new ArgumentException(
                $"Retention policy {request.RetentionPolicyId} does not exist.",
                nameof(request));
        }

        DateTime retentionBase =
            request.ReceivedAt.UtcDateTime.AddYears(
                policy.RetentionYears);

        return DateOnly.FromDateTime(retentionBase);
    }

    private static void AddBusinessReferences(
        ArchiveObject archiveObject,
        IEnumerable<ArchiveBusinessReferenceInput> references,
        DateTimeOffset createdAt)
    {
        foreach (ArchiveBusinessReferenceInput reference in references)
        {
            archiveObject.BusinessReferences.Add(
                new ArchiveBusinessReference
                {
                    BusinessReferenceTypeId =
                        reference.BusinessReferenceTypeId,

                    ReferenceValue =
                        reference.ReferenceValue,

                    BusinessType =
                        reference.BusinessType,

                    Tenant =
                        reference.Tenant,

                    CreatedAt = createdAt
                });
        }
    }

    private string CreateObjectKey(
        ArchiveFileRequest request,
        DateTimeOffset timestamp)
    {
        string prefix =
            _options.ObjectKeyPrefix.Trim('/');

        string fileType =
            SanitizePathSegment(
                request.FileType.ToLowerInvariant());

        string tenant =
            SanitizePathSegment(
                request.Tenant.ToLowerInvariant());

        string objectId =
            Guid.NewGuid().ToString("N");

        return string.Join(
            '/',
            prefix,
            tenant,
            fileType,
            timestamp.UtcDateTime.ToString("yyyy"),
            timestamp.UtcDateTime.ToString("MM"),
            timestamp.UtcDateTime.ToString("dd"),
            objectId);
    }

    private static string SanitizePathSegment(
        string value)
    {
        char[] characters = value
            .Where(character =>
                char.IsLetterOrDigit(character) ||
                character is '-' or '_')
            .ToArray();

        if (characters.Length == 0)
        {
            throw new ArgumentException(
                $"The value '{value}' cannot be used in an object key.");
        }

        return new string(characters);
    }

    private async Task MarkAsErrorBestEffortAsync(
        ArchiveObject archiveObject,
        string errorType,
        string errorMessage,
        string actor)
    {
        try
        {
            archiveObject.ArchiveStatus =
                ArchiveStatus.Error;

            archiveObject.Events.Add(new ArchiveEvent
            {
                EventType =
                    ArchiveEventType.ErrorOccurred,

                EventTimestamp =
                    DateTimeOffset.UtcNow,

                Actor =
                    actor
            });

            archiveObject.Errors.Add(
                new ArchiveErrorQueueItem
                {
                    ErrorType = errorType,
                    ErrorTimestamp = DateTimeOffset.UtcNow,
                    RetryCount = 0,
                    LastErrorMessage = errorMessage,
                    NextRetryAt = null,
                    Resolved = false
                });

            await _dbContext.SaveChangesAsync(
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not persist the failure state for archive object {ArchiveObjectId}.",
                archiveObject.ArchiveObjectId);
        }
    }

    private static JsonDocument CreateIntegrityDetails(
        string expectedHash,
        string actualHash,
        long expectedSizeBytes,
        long actualSizeBytes,
        bool hashMatches,
        bool sizeMatches)
    {
        return JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    expectedSha256Hash = expectedHash,
                    actualSha256Hash = actualHash,
                    expectedSizeBytes,
                    actualSizeBytes,
                    hashMatches,
                    sizeMatches
                }));
    }

    private static void ValidateArchiveRequest(
        ArchiveFileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Content);

        if (!request.Content.CanRead)
        {
            throw new ArgumentException(
                "The content stream must be readable.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.OriginalFilename);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.FileType);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.SourceSystem);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Tenant);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.CreatedBy);
    }

    private static void ValidateSearchRequest(
        ArchiveSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.PageNumber,
                "PageNumber must be at least 1.");
        }

        if (request.PageSize < 1 ||
            request.PageSize > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.PageSize,
                $"PageSize must be between 1 and {MaximumPageSize}.");
        }

        if (request.ReceivedFrom is not null &&
            request.ReceivedUntil is not null &&
            request.ReceivedFrom > request.ReceivedUntil)
        {
            throw new ArgumentException(
                "ReceivedFrom must not be later than ReceivedUntil.",
                nameof(request));
        }

        if (request.ArchivedFrom is not null &&
            request.ArchivedUntil is not null &&
            request.ArchivedFrom > request.ArchivedUntil)
        {
            throw new ArgumentException(
                "ArchivedFrom must not be later than ArchivedUntil.",
                nameof(request));
        }

        if (!string.IsNullOrWhiteSpace(
                request.Sha256Hash))
        {
            string hash =
                request.Sha256Hash.Trim();

            if (hash.Length != 64 ||
                hash.Any(character =>
                    !Uri.IsHexDigit(character)))
            {
                throw new ArgumentException(
                    "Sha256Hash must contain exactly 64 hexadecimal characters.",
                    nameof(request));
            }
        }
    }

    private static void ValidateArchiveObjectId(
        long archiveObjectId)
    {
        if (archiveObjectId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(archiveObjectId),
                archiveObjectId,
                "ArchiveObjectId must be greater than zero.");
        }
    }
}