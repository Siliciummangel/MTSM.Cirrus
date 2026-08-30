using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using System.Security.Cryptography;
using System.Text.Json;

namespace MTSM.Cirrus.Worker;

public sealed class StorageProcessingProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<StorageProcessingProcessor> logger)
{
    private const string ErrorType = "STORAGE_PROCESSING_FAILED";
    private readonly StorageProcessingOptions _options = options.Value;

    private sealed record WorkItem(long ArchiveObjectId);

    public async Task<int> ProcessBatchAsync(
        string workerInstanceId,
        CancellationToken cancellationToken)
    {
        string leaseOwner = $"{workerInstanceId}/{Guid.NewGuid():N}";
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset leaseUntil = now.AddMinutes(_options.LeaseDurationMinutes);

        WorkItem[] workItems = await ClaimBatchAsync(
            leaseOwner,
            now,
            leaseUntil,
            cancellationToken);

        if (workItems.Length == 0)
        {
            return 0;
        }

        logger.LogInformation(
            "Worker {WorkerInstanceId} claimed {Count} staged archive objects.",
            workerInstanceId,
            workItems.Length);

        await Parallel.ForEachAsync(
            workItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxConcurrency,
                CancellationToken = cancellationToken
            },
            async (workItem, itemCancellationToken) =>
                await ProcessOneAsync(
                    workItem.ArchiveObjectId,
                    workerInstanceId,
                    leaseOwner,
                    itemCancellationToken));

        return workItems.Length;
    }

    private async Task<WorkItem[]> ClaimBatchAsync(
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<CirrusDbContext>();

        return await dbContext.Database.SqlQuery<WorkItem>(
            $"""
            WITH candidates AS MATERIALIZED (
                SELECT candidate.archive_object_id
                FROM cirrus.archive_object AS candidate
                INNER JOIN cirrus.tenant AS tenant
                    ON tenant.tenant_id = candidate.tenant_id
                WHERE candidate.staging_object_key IS NOT NULL
                  AND candidate.archive_status = 'Active'
                  AND tenant.status <> 'Disabled'
                  AND (
                        candidate.storage_processing_status = 'Staged'
                        OR (
                            candidate.storage_processing_status = 'RetryPending'
                            AND candidate.storage_processing_next_attempt_at <= {now}
                        )
                        OR (
                            candidate.storage_processing_status = 'Processing'
                            AND candidate.storage_processing_lease_until <= {now}
                        )
                  )
                ORDER BY candidate.received_at, candidate.archive_object_id
                FOR UPDATE OF candidate SKIP LOCKED
                LIMIT {_options.BatchSize}
            ),
            claimed AS (
                UPDATE cirrus.archive_object AS archive_object
                SET storage_processing_status = 'Processing',
                    storage_processing_lease_owner = {leaseOwner},
                    storage_processing_lease_until = {leaseUntil},
                    storage_processing_started_at =
                        COALESCE(storage_processing_started_at, {now}),
                    storage_processing_attempt_count =
                        storage_processing_attempt_count + 1,
                    storage_processing_next_attempt_at = NULL,
                    storage_processing_error_code = NULL,
                    storage_processing_error_message = NULL
                FROM candidates
                WHERE archive_object.archive_object_id =
                    candidates.archive_object_id
                RETURNING archive_object.archive_object_id
            )
            SELECT archive_object_id FROM claimed
            """)
            .ToArrayAsync(cancellationToken);
    }

    private async Task ProcessOneAsync(
        long archiveObjectId,
        string workerInstanceId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        using var leaseCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task heartbeat = RenewLeaseAsync(
            archiveObjectId,
            leaseOwner,
            leaseCancellation,
            cancellationToken);

        try
        {
            await VerifyStagingObjectAsync(
                archiveObjectId,
                workerInstanceId,
                leaseOwner,
                leaseCancellation.Token);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseLeaseAsync(
                archiveObjectId,
                leaseOwner,
                CancellationToken.None);
            throw;
        }
        catch (StagingIntegrityException exception)
        {
            await MarkFailedAsync(
                archiveObjectId,
                workerInstanceId,
                leaseOwner,
                "STAGING_INTEGRITY_MISMATCH",
                exception.Message,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Processing staged archive object {ArchiveObjectId} failed.",
                archiveObjectId);

            await ScheduleRetryOrFailAsync(
                archiveObjectId,
                workerInstanceId,
                leaseOwner,
                exception,
                cancellationToken);
        }
        finally
        {
            await leaseCancellation.CancelAsync();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException)
            {
                // Expected after completion, shutdown or lease loss.
            }
        }
    }

    private async Task VerifyStagingObjectAsync(
        long archiveObjectId,
        string workerInstanceId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        IObjectStorage storage =
            scope.ServiceProvider.GetRequiredService<IObjectStorage>();

        ArchiveObject item = await dbContext.ArchiveObjects
            .AsNoTracking()
            .SingleAsync(candidate =>
                candidate.ArchiveObjectId == archiveObjectId
                && candidate.StorageProcessingStatus ==
                    StorageProcessingStatus.Processing
                && candidate.StorageProcessingLeaseOwner == leaseOwner,
                cancellationToken);

        string stagingObjectKey = item.StagingObjectKey
            ?? throw new InvalidOperationException(
                $"Archive object {archiveObjectId} has no staging object key.");

        await using Stream content = await storage.OpenReadAsync(
            item.BucketName,
            stagingObjectKey,
            cancellationToken);

        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        long actualSize = 0;

        while (true)
        {
            int bytesRead = await content.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, bytesRead);
            actualSize = checked(actualSize + bytesRead);
        }

        string actualHash = Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();

        if (actualSize != item.SizeBytes
            || !string.Equals(
                actualHash,
                item.Sha256Hash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new StagingIntegrityException(
                $"Staging verification failed for archive object {archiveObjectId}.");
        }

        DateTimeOffset verifiedAt = timeProvider.GetUtcNow();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        ArchiveObject? ownedItem = await dbContext.ArchiveObjects
            .Include(candidate => candidate.Errors)
            .SingleOrDefaultAsync(candidate =>
                candidate.ArchiveObjectId == archiveObjectId
                && candidate.StorageProcessingStatus ==
                    StorageProcessingStatus.Processing
                && candidate.StorageProcessingLeaseOwner == leaseOwner,
                cancellationToken);

        if (ownedItem is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogWarning(
                "Storage verification for archive object {ArchiveObjectId} " +
                "finished after its lease was lost.",
                archiveObjectId);
            return;
        }

        ownedItem.StorageProcessingStatus = StorageProcessingStatus.Ready;
        ownedItem.StorageProcessingVerifiedAt = verifiedAt;
        ownedItem.StorageProcessingLeaseOwner = null;
        ownedItem.StorageProcessingLeaseUntil = null;
        ownedItem.StorageProcessingNextAttemptAt = null;
        ownedItem.StorageProcessingErrorCode = null;
        ownedItem.StorageProcessingErrorMessage = null;

        ResolveErrors(ownedItem, verifiedAt);
        ownedItem.Events.Add(new ArchiveEvent
        {
            TenantId = ownedItem.TenantId,
            EventType = ArchiveEventType.StorageProcessingVerified,
            EventTimestamp = verifiedAt,
            Actor = $"archive-worker/{workerInstanceId}"
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RenewLeaseAsync(
        long archiveObjectId,
        string leaseOwner,
        CancellationTokenSource leaseCancellation,
        CancellationToken shutdownToken)
    {
        TimeSpan interval = TimeSpan.FromMinutes(
            Math.Max(1, _options.LeaseDurationMinutes / 3d));

        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(leaseCancellation.Token))
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                CirrusDbContext dbContext =
                    scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
                DateTimeOffset leaseUntil = timeProvider.GetUtcNow()
                    .AddMinutes(_options.LeaseDurationMinutes);

                int updated = await dbContext.ArchiveObjects
                    .Where(item =>
                        item.ArchiveObjectId == archiveObjectId
                        && item.StorageProcessingStatus ==
                            StorageProcessingStatus.Processing
                        && item.StorageProcessingLeaseOwner == leaseOwner)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            item => item.StorageProcessingLeaseUntil,
                            leaseUntil),
                        shutdownToken);

                if (updated == 0)
                {
                    await leaseCancellation.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
            when (leaseCancellation.IsCancellationRequested
                || shutdownToken.IsCancellationRequested)
        {
            // Expected after completion, shutdown or lease loss.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Renewing storage-processing lease for archive object " +
                "{ArchiveObjectId} failed.",
                archiveObjectId);
            await leaseCancellation.CancelAsync();
        }
    }

    private async Task ScheduleRetryOrFailAsync(
        long archiveObjectId,
        string workerInstanceId,
        string leaseOwner,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<CirrusDbContext>();

        ArchiveObject? item = await dbContext.ArchiveObjects
            .Include(candidate => candidate.Errors)
            .SingleOrDefaultAsync(candidate =>
                candidate.ArchiveObjectId == archiveObjectId
                && candidate.StorageProcessingLeaseOwner == leaseOwner,
                cancellationToken);

        if (item is null)
        {
            return;
        }

        if (item.StorageProcessingAttemptCount >= _options.MaximumAttempts)
        {
            await ApplyFailedStateAsync(
                dbContext,
                item,
                workerInstanceId,
                "STORAGE_PROCESSING_ATTEMPTS_EXHAUSTED",
                "Storage processing exhausted its retry limit.",
                cancellationToken);
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        double exponentialSeconds = _options.InitialRetryDelaySeconds
            * Math.Pow(2, Math.Max(0, item.StorageProcessingAttemptCount - 1));
        TimeSpan delay = TimeSpan.FromSeconds(Math.Min(
            exponentialSeconds,
            TimeSpan.FromMinutes(_options.MaximumRetryDelayMinutes).TotalSeconds));
        DateTimeOffset retryAt = now.Add(delay);
        string safeMessage =
            $"Storage processing failed with {exception.GetType().Name}.";

        item.StorageProcessingStatus = StorageProcessingStatus.RetryPending;
        item.StorageProcessingNextAttemptAt = retryAt;
        item.StorageProcessingLeaseOwner = null;
        item.StorageProcessingLeaseUntil = null;
        item.StorageProcessingErrorCode = ErrorType;
        item.StorageProcessingErrorMessage = safeMessage;
        UpsertError(item, now, retryAt, safeMessage);
        AddFailureEvent(item, workerInstanceId, ErrorType, retryAt, now);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        long archiveObjectId,
        string workerInstanceId,
        string leaseOwner,
        string errorCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        ArchiveObject? item = await dbContext.ArchiveObjects
            .Include(candidate => candidate.Errors)
            .SingleOrDefaultAsync(candidate =>
                candidate.ArchiveObjectId == archiveObjectId
                && candidate.StorageProcessingLeaseOwner == leaseOwner,
                cancellationToken);

        if (item is not null)
        {
            await ApplyFailedStateAsync(
                dbContext,
                item,
                workerInstanceId,
                errorCode,
                safeMessage,
                cancellationToken);
        }
    }

    private async Task ApplyFailedStateAsync(
        CirrusDbContext dbContext,
        ArchiveObject item,
        string workerInstanceId,
        string errorCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        item.StorageProcessingStatus = StorageProcessingStatus.Failed;
        item.StorageProcessingLeaseOwner = null;
        item.StorageProcessingLeaseUntil = null;
        item.StorageProcessingNextAttemptAt = null;
        item.StorageProcessingErrorCode = errorCode;
        item.StorageProcessingErrorMessage = safeMessage;
        UpsertError(item, now, null, safeMessage);
        AddFailureEvent(item, workerInstanceId, errorCode, null, now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseLeaseAsync(
        long archiveObjectId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            CirrusDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
            await dbContext.ArchiveObjects
                .Where(item =>
                    item.ArchiveObjectId == archiveObjectId
                    && item.StorageProcessingLeaseOwner == leaseOwner)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            item => item.StorageProcessingStatus,
                            StorageProcessingStatus.Staged)
                        .SetProperty(
                            item => item.StorageProcessingLeaseOwner,
                            (string?)null)
                        .SetProperty(
                            item => item.StorageProcessingLeaseUntil,
                            (DateTimeOffset?)null),
                    cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Releasing storage-processing lease for archive object " +
                "{ArchiveObjectId} failed; it will expire.",
                archiveObjectId);
        }
    }

    private static void ResolveErrors(
        ArchiveObject item,
        DateTimeOffset resolvedAt)
    {
        foreach (ArchiveErrorQueueItem error in item.Errors.Where(error =>
                     error.ErrorType == ErrorType && !error.Resolved))
        {
            error.Resolved = true;
            error.ResolvedAt = resolvedAt;
            error.NextRetryAt = null;
        }
    }

    private static void UpsertError(
        ArchiveObject item,
        DateTimeOffset now,
        DateTimeOffset? retryAt,
        string safeMessage)
    {
        ArchiveErrorQueueItem? error = item.Errors
            .Where(candidate =>
                candidate.ErrorType == ErrorType && !candidate.Resolved)
            .OrderByDescending(candidate => candidate.ErrorTimestamp)
            .FirstOrDefault();

        if (error is null)
        {
            item.Errors.Add(new ArchiveErrorQueueItem
            {
                ErrorType = ErrorType,
                ErrorTimestamp = now,
                RetryCount = 1,
                LastErrorMessage = safeMessage,
                NextRetryAt = retryAt,
                Resolved = false
            });
        }
        else
        {
            error.ErrorTimestamp = now;
            error.RetryCount++;
            error.LastErrorMessage = safeMessage;
            error.NextRetryAt = retryAt;
        }
    }

    private static void AddFailureEvent(
        ArchiveObject item,
        string workerInstanceId,
        string errorCode,
        DateTimeOffset? retryAt,
        DateTimeOffset now)
    {
        item.Events.Add(new ArchiveEvent
        {
            TenantId = item.TenantId,
            EventType = ArchiveEventType.StorageProcessingFailed,
            EventTimestamp = now,
            Actor = $"archive-worker/{workerInstanceId}",
            DetailsJson = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                errorCode,
                retryAt
            }))
        });
    }

    private sealed class StagingIntegrityException(string message)
        : Exception(message);
}
