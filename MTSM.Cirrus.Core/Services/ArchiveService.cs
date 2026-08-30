using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MTSM.Cirrus.Core.Abstractions;
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
    private const int MaximumActorLength = 255;
    private const int MaximumBusinessReferenceValueLength = 255;
    private const int MaximumBusinessReferences = 1000;
    private const int MaximumBusinessTypeLength = 50;
    private const int MaximumCreatedByLength = 255;
    private const int MaximumFileTypeLength = 100;
    private const int MaximumMimeTypeLength = 255;
    private const int MaximumOriginalFilenameLength = 1024;
    private const int MaximumPartnerLength = 255;
    private const int MaximumSourceSystemLength = 255;

    private readonly CirrusDbContext _dbContext;
    private readonly IObjectStorage _objectStorage;
    private readonly IManifestContentReader _manifestContentReader;
    private readonly ILogger<ArchiveService> _logger;

    public ArchiveService(
        CirrusDbContext dbContext,
        IObjectStorage objectStorage,
        IManifestContentReader manifestContentReader,
        ILogger<ArchiveService> logger)
    {
        _dbContext = dbContext;
        _objectStorage = objectStorage;
        _manifestContentReader = manifestContentReader;
        _logger = logger;
    }

    public async Task<ArchiveFileResult> ArchiveAsync(
        ArchiveFileRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveRequest(request);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string originalFilename = request.OriginalFilename.Trim();
        string fileType = request.FileType.Trim();
        string? mimeType = NormalizeOptionalValue(request.MimeType);
        string sourceSystem = request.SourceSystem.Trim();
        string? partner = NormalizeOptionalValue(request.Partner);
        string createdBy = request.CreatedBy.Trim();

        DateOnly retentionUntil;
        Tenant tenant;

        try
        {
            tenant = await GetActiveTenantAsync(request.TenantId, cancellationToken);
            retentionUntil = await ResolveRetentionUntilAsync(
                request,
                tenant,
                cancellationToken);

            await ValidateBusinessReferenceTypesAsync(
                request.BusinessReferences,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Validating archive metadata against the database failed.");

            throw new ArchiveException(
                "Validating archive metadata failed.",
                exception);
        }

        string stagingObjectKey = CreateStagingObjectKey(
            tenant,
            fileType,
            now);

        var archiveObject = new ArchiveObject
        {
            TenantId = tenant.TenantId,
            ObjectKey = null,
            StagingObjectKey = stagingObjectKey,
            StorageProcessingStatus = StorageProcessingStatus.Staged,
            BucketName = tenant.BucketName,

            FileType = fileType,
            MimeType = mimeType,
            SourceSystem = sourceSystem,
            Partner = partner,
            OriginalFilename = originalFilename,

            SizeBytes = 0,

            ReceivedAt = request.ReceivedAt,
            ArchivedAt = null,

            RetentionPolicyId = request.RetentionPolicyId
                ?? tenant.DefaultRetentionPolicyId,
            RetentionUntil = retentionUntil,

            ArchiveStatus = ArchiveStatus.Pending,

            IsWormProtected = false,
            EncryptionKeyId = tenant.EncryptionKeyId,
            CreatedBy = createdBy
        };

        AddBusinessReferences(
            archiveObject,
            request.BusinessReferences,
            now);

        archiveObject.Events.Add(new ArchiveEvent
        {
            TenantId = archiveObject.TenantId,
            EventType = ArchiveEventType.Created,
            EventTimestamp = now,
            Actor = createdBy
        });

        _dbContext.ArchiveObjects.Add(archiveObject);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
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
                "Creating the pending archive record failed.");

            throw new ArchiveException(
                "Creating the pending archive record failed.",
                exception);
        }

        try
        {
            await using var hashingStream = new HashingReadStream(
                request.Content,
                leaveOpen: true);

            ObjectStorageWriteResult storageResult =
                await _objectStorage.WriteAsync(
                    archiveObject.BucketName,
                    GetCurrentStorageObjectKey(archiveObject),
                    hashingStream,
                    archiveObject.MimeType,
                    archiveObject.EncryptionKeyId,
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
                TenantId = archiveObject.TenantId,
                EventType = ArchiveEventType.Archived,
                EventTimestamp = archivedAt,
                Actor = request.CreatedBy
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Archive object {ArchiveObjectId} was stored successfully.",
                archiveObject.ArchiveObjectId);

            return new ArchiveFileResult(
                archiveObject.ArchiveObjectId,
                archiveObject.TenantId,
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
                createdBy);

            throw;
        }
        catch (Exception exception)
        {
            if (exception is ObjectStorageException)
            {
                _logger.LogError(
                    "Object storage failed while archiving object {ArchiveObjectId} " +
                    "at {BucketName}/{ObjectKey}.",
                    archiveObject.ArchiveObjectId,
                    archiveObject.BucketName,
                    archiveObject.StagingObjectKey);
            }
            else
            {
                _logger.LogError(
                    exception,
                    "Finalizing archive object {ArchiveObjectId} failed.",
                    archiveObject.ArchiveObjectId);
            }

            await MarkAsErrorBestEffortAsync(
                archiveObject,
                "ARCHIVE_FAILED",
                "The archive content could not be stored or finalized.",
                createdBy);

            throw new ArchiveException(
                $"Archiving object {archiveObject.ArchiveObjectId} failed.",
                exception);
        }
    }

    public async Task<ArchiveDownloadResult> DownloadAsync(
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveObjectId(archiveObjectId);
        string normalizedActor = NormalizeRequiredValue(
            actor,
            nameof(actor),
            MaximumActorLength);

        ArchiveObject archiveObject =
            await GetActiveArchiveObjectAsync(
                tenantId,
                archiveObjectId,
                cancellationToken);

        Stream content;

        try
        {
            content = archiveObject.ContentManifestId is long manifestId
                && archiveObject.StorageProcessingStatus == StorageProcessingStatus.Completed
                ? await _manifestContentReader.OpenReadAsync(manifestId, cancellationToken)
                : await _objectStorage.OpenReadAsync(
                    archiveObject.BucketName,
                    GetCurrentStorageObjectKey(archiveObject),
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
                "Opening archive object {ArchiveObjectId} from object storage failed " +
                "at {BucketName}/{ObjectKey} with error type {StorageErrorType}.",
                archiveObjectId,
                archiveObject.BucketName,
                GetStorageDiagnosticLocation(archiveObject),
                exception.GetType().Name);

            throw new ArchiveException(
                $"Opening archive object {archiveObjectId} failed.",
                exception);
        }

        if (content is null)
        {
            throw new ArchiveException(
                $"Object storage returned no stream for archive object {archiveObjectId}.");
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
            TenantId = archiveObject.TenantId,
            EventType = ArchiveEventType.Downloaded,
            EventTimestamp = DateTimeOffset.UtcNow,
            Actor = normalizedActor
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            await content.DisposeAsync();
            throw;
        }
        catch (Exception exception)
        {
            await content.DisposeAsync();

            _logger.LogError(
                exception,
                "Recording the download event for archive object {ArchiveObjectId} failed.",
                archiveObjectId);

            throw new ArchiveException(
                $"Opening archive object {archiveObjectId} for download failed.",
                exception);
        }

        _logger.LogInformation(
            "Archive object {ArchiveObjectId} was opened for download.",
            archiveObjectId);

        return new ArchiveDownloadResult(
            archiveObject.ArchiveObjectId,
            archiveObject.OriginalFilename,
            archiveObject.MimeType,
            archiveObject.SizeBytes,
            archiveObject.Sha256Hash!,
            content);
    }

    public async Task<ArchiveMetadataResult?> GetMetadataAsync(
        long tenantId,
        long archiveObjectId,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveObjectId(archiveObjectId);
        ValidateTenantId(tenantId);

        return await _dbContext.ArchiveObjects
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId
                && x.Tenant.Status != TenantStatus.Disabled
                && x.ArchiveObjectId == archiveObjectId)
            .Select(x => new ArchiveMetadataResult(
                x.ArchiveObjectId,
                x.TenantId,
                x.StagingObjectKey ?? x.ObjectKey!,
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
                x.StorageProcessingStatus,
                x.DeletionRequestedAt,
                x.DeletionRequestedBy,
                x.PurgedAt,
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
        long tenantId,
        ArchiveSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateSearchRequest(request);
        ValidateTenantId(tenantId);

        IQueryable<ArchiveObject> query =
            _dbContext.ArchiveObjects
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId
                    && x.Tenant.Status != TenantStatus.Disabled);

        query = ApplySearchFilters(
            query,
            tenantId,
            request);

        long totalCount =
            await query.LongCountAsync(cancellationToken);

        int totalPages = totalCount == 0
            ? 0
            : checked((int)Math.Ceiling(
                totalCount / (double)request.PageSize));

        int skip = (request.PageNumber - 1) * request.PageSize;

        ArchiveSearchItem[] items =
            await query
                .OrderByDescending(x => x.ReceivedAt)
                .ThenByDescending(x => x.ArchiveObjectId)
                .Skip(skip)
                .Take(request.PageSize)
                .Select(x => new ArchiveSearchItem(
                    x.ArchiveObjectId,
                    x.TenantId,
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
                    x.DeletionRequestedAt,
                    x.PurgedAt,

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
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveObjectId(archiveObjectId);
        string normalizedActor = NormalizeRequiredValue(
            actor,
            nameof(actor),
            MaximumActorLength);

        ArchiveObject archiveObject =
            await GetActiveArchiveObjectAsync(
                tenantId,
                archiveObjectId,
                cancellationToken);

        Stream content;

        try
        {
            content = archiveObject.ContentManifestId is long manifestId
                && archiveObject.StorageProcessingStatus == StorageProcessingStatus.Completed
                ? await _manifestContentReader.OpenReadAsync(manifestId, cancellationToken)
                : await _objectStorage.OpenReadAsync(
                    archiveObject.BucketName,
                    GetCurrentStorageObjectKey(archiveObject),
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
                "Opening archive object {ArchiveObjectId} for integrity verification " +
                "failed at {BucketName}/{ObjectKey} with error type {StorageErrorType}.",
                archiveObjectId,
                archiveObject.BucketName,
                GetStorageDiagnosticLocation(archiveObject),
                exception.GetType().Name);

            throw new ArchiveException(
                $"Opening archive object {archiveObjectId} for integrity verification failed.",
                exception);
        }

        if (content is null)
        {
            throw new ArchiveException(
                $"Object storage returned no stream for archive object {archiveObjectId}.");
        }

        await using (content)
        {

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

            try
            {
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

                    actualSizeBytes = checked(actualSizeBytes + bytesRead);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Reading archive object {ArchiveObjectId} for integrity verification " +
                    "failed at {BucketName}/{ObjectKey} with error type {StorageErrorType}.",
                    archiveObjectId,
                    archiveObject.BucketName,
                    GetStorageDiagnosticLocation(archiveObject),
                    exception.GetType().Name);

                throw new ArchiveException(
                    $"Reading archive object {archiveObjectId} for integrity verification failed.",
                    exception);
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
                TenantId = archiveObject.TenantId,
                EventType = isValid
                ? ArchiveEventType.IntegrityVerified
                : ArchiveEventType.IntegrityCheckFailed,

                EventTimestamp = verifiedAt,
                Actor = normalizedActor,

                DetailsJson = CreateIntegrityDetails(
                expectedSha256Hash,
                actualSha256Hash,
                archiveObject.SizeBytes,
                actualSizeBytes,
                hashMatches,
                sizeMatches)
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
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
                    "Recording the integrity result for archive object {ArchiveObjectId} failed.",
                    archiveObjectId);

                throw new ArchiveException(
                    $"Recording the integrity result for archive object {archiveObjectId} failed.",
                    exception);
            }

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
                    "Hash matched: {HashMatches}; size matched: {SizeMatches}.",
                    archiveObjectId,
                    hashMatches,
                    sizeMatches);
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
    }

    public async Task<ArchiveIntegrityStatusResult?> GetIntegrityStatusAsync(
        long tenantId,
        long archiveObjectId,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveObjectId(archiveObjectId);
        ValidateTenantId(tenantId);

        ArchiveObject? archiveObject =
            await _dbContext.ArchiveObjects
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.TenantId == tenantId
                        && item.Tenant.Status != TenantStatus.Disabled
                        && item.ArchiveObjectId == archiveObjectId,
                    cancellationToken);

        if (archiveObject is null)
        {
            return null;
        }

        ArchiveEvent? lastCheck =
            await _dbContext.ArchiveEvents
                .AsNoTracking()
                .Where(archiveEvent =>
                    archiveEvent.TenantId == tenantId
                    && archiveEvent.ArchiveObjectId == archiveObjectId
                    && (archiveEvent.EventType ==
                            ArchiveEventType.IntegrityVerified
                        || archiveEvent.EventType ==
                            ArchiveEventType.IntegrityCheckFailed))
                .OrderByDescending(archiveEvent =>
                    archiveEvent.EventTimestamp)
                .ThenByDescending(archiveEvent =>
                    archiveEvent.ArchiveEventId)
                .FirstOrDefaultAsync(cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool isCheckInProgress =
            archiveObject.IntegrityCheckLeaseOwner is not null
            && archiveObject.IntegrityCheckLeaseUntil > now;

        return new ArchiveIntegrityStatusResult(
            archiveObject.ArchiveObjectId,
            lastCheck?.EventTimestamp,
            lastCheck is null
                ? null
                : lastCheck.EventType ==
                    ArchiveEventType.IntegrityVerified,
            lastCheck?.Actor,
            archiveObject.NextIntegrityCheckAt,
            isCheckInProgress,
            archiveObject.IntegrityCheckLeaseOwner,
            archiveObject.IntegrityCheckLeaseUntil);
    }

    public async Task<ArchiveDeletionRequestResult> RequestDeletionAsync(
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveObjectId(archiveObjectId);
        ValidateTenantId(tenantId);
        string normalizedActor = NormalizeRequiredValue(
            actor,
            nameof(actor),
            MaximumActorLength);

        var executionStrategy =
            _dbContext.Database.CreateExecutionStrategy();

        try
        {
            return await executionStrategy.ExecuteAsync(
                async () =>
                {
                    _dbContext.ChangeTracker.Clear();

                    await using var transaction =
                        await _dbContext.Database.BeginTransactionAsync(
                            cancellationToken);

                    ArchiveObject? archiveObject =
                        await _dbContext.ArchiveObjects
                            .FromSqlInterpolated(
                                $"""
                            SELECT *
                            FROM cirrus.archive_object
                            WHERE tenant_id = {tenantId}
                              AND archive_object_id = {archiveObjectId}
                              AND EXISTS (
                                  SELECT 1
                                  FROM cirrus.tenant
                                  WHERE tenant.tenant_id = archive_object.tenant_id
                                    AND tenant.status <> 'Disabled')
                            FOR UPDATE
                            """)
                            .SingleOrDefaultAsync(cancellationToken);

                    if (archiveObject is null)
                    {
                        throw new ArchiveObjectNotFoundException(
                            archiveObjectId);
                    }

                    ArchiveDeletionRequestResult result;

                    switch (archiveObject.ArchiveStatus)
                    {
                        case ArchiveStatus.Active:
                            {
                                DateTimeOffset requestedAt =
                                DateTimeOffset.UtcNow;

                                archiveObject.ArchiveStatus =
                                ArchiveStatus.DeletionRequested;

                                archiveObject.DeletionRequestedAt =
                                requestedAt;

                                archiveObject.DeletionRequestedBy =
                                normalizedActor;

                                archiveObject.Events.Add(
                                new ArchiveEvent
                                {
                                    TenantId = archiveObject.TenantId,
                                    EventType =
                                        ArchiveEventType.DeletionRequested,

                                    EventTimestamp =
                                        requestedAt,

                                    Actor =
                                        normalizedActor,

                                    DetailsJson =
                                        CreateDeletionRequestedDetails(
                                            archiveObject)
                                });

                                await _dbContext.SaveChangesAsync(
                                cancellationToken);

                                result = new ArchiveDeletionRequestResult(
                                archiveObject.ArchiveObjectId,
                                archiveObject.ArchiveStatus,
                                archiveObject.DeletionRequestedAt,
                                archiveObject.DeletionRequestedBy,
                                archiveObject.PurgedAt,
                                StateChanged: true);

                                break;
                            }

                        case ArchiveStatus.DeletionRequested:
                            {
                                result = new ArchiveDeletionRequestResult(
                                archiveObject.ArchiveObjectId,
                                archiveObject.ArchiveStatus,
                                archiveObject.DeletionRequestedAt,
                                archiveObject.DeletionRequestedBy,
                                archiveObject.PurgedAt,
                                StateChanged: false);

                                break;
                            }

                        case ArchiveStatus.Purged:
                            {
                                result = new ArchiveDeletionRequestResult(
                                archiveObject.ArchiveObjectId,
                                archiveObject.ArchiveStatus,
                                archiveObject.DeletionRequestedAt,
                                archiveObject.DeletionRequestedBy,
                                archiveObject.PurgedAt,
                                StateChanged: false);

                                break;
                            }

                        case ArchiveStatus.Pending:
                        case ArchiveStatus.Error:
                        default:
                            throw new ArchiveObjectUnavailableException(
                                archiveObjectId,
                                archiveObject.ArchiveStatus);
                    }

                    await transaction.CommitAsync(
                        cancellationToken);

                    return result;
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArchiveException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Requesting deletion of archive object {ArchiveObjectId} failed.",
                archiveObjectId);

            throw new ArchiveException(
                $"Requesting deletion of archive object {archiveObjectId} failed.",
                exception);
        }
    }

    private async Task<ArchiveObject> GetActiveArchiveObjectAsync(
        long tenantId,
        long archiveObjectId,
        CancellationToken cancellationToken)
    {
        ValidateTenantId(tenantId);
        ArchiveObject? archiveObject;

        try
        {
            archiveObject = await _dbContext.ArchiveObjects
                .SingleOrDefaultAsync(
                    x => x.TenantId == tenantId
                        && x.Tenant.Status != TenantStatus.Disabled
                        && x.ArchiveObjectId == archiveObjectId,
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
                "Loading archive object {ArchiveObjectId} failed.",
                archiveObjectId);

            throw new ArchiveException(
                $"Loading archive object {archiveObjectId} failed.",
                exception);
        }

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

    private async Task<Tenant> GetActiveTenantAsync(
        long tenantId,
        CancellationToken cancellationToken)
    {
        ValidateTenantId(tenantId);
        Tenant? tenant = _dbContext.Tenants.Local
            .SingleOrDefault(item => item.TenantId == tenantId);

        tenant ??= await _dbContext.Tenants
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.TenantId == tenantId,
                    cancellationToken);

        if (tenant is null)
        {
            throw new ArgumentException(
                $"Tenant '{tenantId}' does not exist.",
                nameof(tenantId));
        }

        if (tenant.Status != TenantStatus.Active)
        {
            throw new ArchiveException(
                $"Tenant '{tenantId}' is not active.");
        }

        return tenant;
    }

    private static IQueryable<ArchiveObject> ApplySearchFilters(
        IQueryable<ArchiveObject> query,
        long tenantId,
        ArchiveSearchRequest request)
    {
        if (request.ArchiveObjectId is not null)
        {
            query = query.Where(x =>
                x.ArchiveObjectId ==
                request.ArchiveObjectId.Value);
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
            string escapedOriginalFilename =
                EscapeLikePattern(originalFilename);

            query = query.Where(x =>
                EF.Functions.ILike(
                    x.OriginalFilename,
                    $"%{escapedOriginalFilename}%",
                    "\\"));
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
        else
        {
            query = query.Where(x =>
                x.ArchiveStatus ==
                ArchiveStatus.Active);
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

        bool hasBusinessReferenceFilter =
            request.BusinessReferenceTypeId is not null
            || !string.IsNullOrWhiteSpace(request.BusinessReferenceValue)
            || !string.IsNullOrWhiteSpace(request.BusinessType);

        if (hasBusinessReferenceFilter)
        {
            int? referenceTypeId = request.BusinessReferenceTypeId;
            string? referenceValue =
                NormalizeOptionalValue(request.BusinessReferenceValue);
            string? businessType =
                NormalizeOptionalValue(request.BusinessType);

            query = query.Where(x =>
                x.BusinessReferences.Any(reference =>
                    reference.TenantId == tenantId
                    && (referenceTypeId == null
                        || reference.BusinessReferenceTypeId == referenceTypeId)
                    && (referenceValue == null
                        || reference.ReferenceValue == referenceValue)
                    && (businessType == null
                        || reference.BusinessType == businessType)));
        }

        return query;
    }

    private async Task<DateOnly> ResolveRetentionUntilAsync(
        ArchiveFileRequest request,
        Tenant tenant,
        CancellationToken cancellationToken)
    {
        RetentionPolicy? policy = null;

        int? retentionPolicyId = request.RetentionPolicyId
            ?? tenant.DefaultRetentionPolicyId;

        if (retentionPolicyId is not null)
        {
            policy = await _dbContext.RetentionPolicies
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.RetentionPolicyId == retentionPolicyId.Value,
                    cancellationToken);

            if (policy is null)
            {
                throw new ArgumentException(
                    $"Retention policy {retentionPolicyId} does not exist.",
                    nameof(request));
            }
        }

        if (request.RetentionUntil is not null)
        {
            return request.RetentionUntil.Value;
        }

        if (policy is null)
        {
            throw new ArgumentException(
                "Either RetentionUntil, RetentionPolicyId or a tenant default " +
                "retention policy must be supplied.",
                nameof(request));
        }

        DateTime retentionBase = request.ReceivedAt.UtcDateTime;

        if (policy.RetentionYears > DateTime.MaxValue.Year - retentionBase.Year)
        {
            throw new ArchiveException(
                $"Retention policy {policy.RetentionPolicyId} exceeds the supported date range.");
        }

        retentionBase = retentionBase.AddYears(policy.RetentionYears);

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
                    TenantId = archiveObject.TenantId,

                    BusinessReferenceTypeId =
                        reference.BusinessReferenceTypeId,

                    ReferenceValue =
                        reference.ReferenceValue.Trim(),

                    BusinessType =
                        reference.BusinessType.Trim(),

                    CreatedAt = createdAt
                });
        }
    }

    private static JsonDocument CreateDeletionRequestedDetails(
    ArchiveObject archiveObject)
    {
        return JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    previousStatus =
                        ArchiveStatus.Active.ToString(),

                    newStatus =
                        ArchiveStatus.DeletionRequested.ToString(),

                    retentionUntil =
                        archiveObject.RetentionUntil,

                    isWormProtected =
                        archiveObject.IsWormProtected
                }));
    }

    private static string CreateStagingObjectKey(
        Tenant tenant,
        string fileTypeValue,
        DateTimeOffset timestamp)
    {
        string prefix =
            tenant.ObjectKeyPrefix.Trim('/');

        string fileType =
            SanitizePathSegment(
                fileTypeValue.ToLowerInvariant());

        string objectId =
            Guid.NewGuid().ToString("N");

        return string.Join(
            '/',
            prefix,
            "staging",
            fileType,
            timestamp.UtcDateTime.ToString("yyyy"),
            timestamp.UtcDateTime.ToString("MM"),
            timestamp.UtcDateTime.ToString("dd"),
            objectId);
    }

    private static string GetCurrentStorageObjectKey(ArchiveObject archiveObject) =>
        archiveObject.StagingObjectKey
        ?? archiveObject.ObjectKey
        ?? throw new ArchiveException(
            $"Archive object {archiveObject.ArchiveObjectId} has no readable storage location.");

    private static string GetStorageDiagnosticLocation(ArchiveObject archiveObject) =>
        archiveObject.StagingObjectKey
        ?? archiveObject.ObjectKey
        ?? $"manifest:{archiveObject.ContentManifestId}";

    private async Task ValidateBusinessReferenceTypesAsync(
        IReadOnlyCollection<ArchiveBusinessReferenceInput> references,
        CancellationToken cancellationToken)
    {
        int[] requestedTypeIds = references
            .Select(reference => reference.BusinessReferenceTypeId)
            .Distinct()
            .ToArray();

        if (requestedTypeIds.Length == 0)
        {
            return;
        }

        int[] existingTypeIds = await _dbContext.BusinessReferenceTypes
            .AsNoTracking()
            .Where(type => requestedTypeIds.Contains(type.BusinessReferenceTypeId))
            .Select(type => type.BusinessReferenceTypeId)
            .ToArrayAsync(cancellationToken);

        int[] missingTypeIds = requestedTypeIds
            .Except(existingTypeIds)
            .OrderBy(id => id)
            .ToArray();

        if (missingTypeIds.Length > 0)
        {
            throw new ArgumentException(
                $"Business reference types do not exist: {string.Join(", ", missingTypeIds)}.",
                nameof(references));
        }
    }

    private static string SanitizePathSegment(
        string value)
    {
        char[] characters = value
            .Where(character =>
                character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-' or '_')
            .ToArray();

        if (characters.Length == 0)
        {
            throw new ArgumentException(
                "The value cannot be represented safely in an object key.");
        }

        return new string(characters);
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private async Task MarkAsErrorBestEffortAsync(
        ArchiveObject archiveObject,
        string errorType,
        string errorMessage,
        string actor)
    {
        try
        {
            using var persistenceTimeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(10));

            archiveObject.ArchiveStatus =
                ArchiveStatus.Error;

            archiveObject.Events.Add(new ArchiveEvent
            {
                TenantId = archiveObject.TenantId,
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
                persistenceTimeout.Token);
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

        NormalizeRequiredValue(
            request.OriginalFilename,
            nameof(request.OriginalFilename),
            MaximumOriginalFilenameLength);

        if (request.OriginalFilename.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException(
                "OriginalFilename must not contain path separators.",
                nameof(request));
        }
        NormalizeRequiredValue(
            request.FileType,
            nameof(request.FileType),
            MaximumFileTypeLength);
        ValidateOptionalValue(
            request.MimeType,
            nameof(request.MimeType),
            MaximumMimeTypeLength);
        NormalizeRequiredValue(
            request.SourceSystem,
            nameof(request.SourceSystem),
            MaximumSourceSystemLength);
        ValidateOptionalValue(
            request.Partner,
            nameof(request.Partner),
            MaximumPartnerLength);
        ValidateTenantId(request.TenantId);
        NormalizeRequiredValue(
            request.CreatedBy,
            nameof(request.CreatedBy),
            MaximumCreatedByLength);

        if (request.RetentionPolicyId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.RetentionPolicyId,
                "RetentionPolicyId must be greater than zero.");
        }

        if (request.ReceivedAt == default)
        {
            throw new ArgumentException(
                "ReceivedAt must be supplied.",
                nameof(request));
        }

        if (request.RetentionUntil is not null
            && request.RetentionUntil < DateOnly.FromDateTime(request.ReceivedAt.UtcDateTime))
        {
            throw new ArgumentException(
                "RetentionUntil must not be earlier than ReceivedAt.",
                nameof(request));
        }

        if (request.BusinessReferences is null)
        {
            throw new ArgumentException(
                "BusinessReferences must not be null.",
                nameof(request));
        }

        if (request.BusinessReferences.Count > MaximumBusinessReferences)
        {
            throw new ArgumentException(
                $"BusinessReferences must not contain more than {MaximumBusinessReferences} entries.",
                nameof(request));
        }

        foreach (ArchiveBusinessReferenceInput reference in
                 request.BusinessReferences)
        {
            if (reference is null)
            {
                throw new ArgumentException(
                    "BusinessReferences must not contain null entries.",
                    nameof(request));
            }

            if (reference.BusinessReferenceTypeId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    reference.BusinessReferenceTypeId,
                    "BusinessReferenceTypeId must be greater than zero.");
            }

            NormalizeRequiredValue(
                reference.ReferenceValue,
                nameof(reference.ReferenceValue),
                MaximumBusinessReferenceValueLength);
            NormalizeRequiredValue(
                reference.BusinessType,
                nameof(reference.BusinessType),
                MaximumBusinessTypeLength);
        }

        var duplicateReference = request.BusinessReferences
            .GroupBy(reference => new
            {
                reference.BusinessReferenceTypeId,
                ReferenceValue = reference.ReferenceValue.Trim(),
                BusinessType = reference.BusinessType.Trim()
            })
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateReference is not null)
        {
            throw new ArgumentException(
                "BusinessReferences must not contain duplicate entries.",
                nameof(request));
        }
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

        if (request.PageNumber > ((int.MaxValue - 1) / request.PageSize) + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.PageNumber,
                "The requested page exceeds the supported pagination range.");
        }

        if (request.ArchiveObjectId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.ArchiveObjectId,
                "ArchiveObjectId must be greater than zero.");
        }

        if (request.BusinessReferenceTypeId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.BusinessReferenceTypeId,
                "BusinessReferenceTypeId must be greater than zero.");
        }

        ValidateOptionalValue(request.FileType, nameof(request.FileType), MaximumFileTypeLength);
        ValidateOptionalValue(request.SourceSystem, nameof(request.SourceSystem), MaximumSourceSystemLength);
        ValidateOptionalValue(request.Partner, nameof(request.Partner), MaximumPartnerLength);
        ValidateOptionalValue(request.OriginalFilename, nameof(request.OriginalFilename), MaximumOriginalFilenameLength);
        ValidateOptionalValue(request.BusinessReferenceValue, nameof(request.BusinessReferenceValue), MaximumBusinessReferenceValueLength);
        ValidateOptionalValue(request.BusinessType, nameof(request.BusinessType), MaximumBusinessTypeLength);

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

    private static string NormalizeRequiredValue(
        string? value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The value must not be empty or whitespace.",
                parameterName);
        }

        string normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value must not exceed {maximumLength} characters.",
                parameterName);
        }

        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The value must not contain control characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void ValidateTenantId(long tenantId)
    {
        if (tenantId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tenantId),
                tenantId,
                "TenantId must be greater than zero.");
        }
    }

    private static void ValidateOptionalValue(
        string? value,
        string parameterName,
        int maximumLength)
    {
        string? normalized = NormalizeOptionalValue(value);

        if (normalized?.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value must not exceed {maximumLength} characters.",
                parameterName);
        }

        if (normalized?.Any(char.IsControl) == true)
        {
            throw new ArgumentException(
                "The value must not contain control characters.",
                parameterName);
        }
    }
}
