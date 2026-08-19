using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Models;
using System.Text.Json;

namespace MTSM.Cirrus.Worker;

public sealed class PurgeProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<PurgeOptions> options,
    TimeProvider timeProvider,
    ILogger<PurgeProcessor> logger)
{
    private const string PurgeErrorType = "PURGE_FAILED";
    private sealed record WorkItem(long ArchiveObjectId);
    private readonly PurgeOptions _options = options.Value;

    public async Task<int> ProcessBatchAsync(
        string workerInstanceId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        string leaseOwner = $"{workerInstanceId}/{Guid.NewGuid():N}";
        DateTimeOffset leaseUntil = now.AddMinutes(_options.LeaseDurationMinutes);

        await RequestExpiredPolicyDeletionsAsync(now, today, cancellationToken);

        WorkItem[] workItems = await ClaimBatchAsync(
            leaseOwner,
            now,
            today,
            leaseUntil,
            cancellationToken);

        await Parallel.ForEachAsync(
            workItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxConcurrentDeletes,
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

    private async Task RequestExpiredPolicyDeletionsAsync(
        DateTimeOffset now,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext dbContext = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        const string actor = "archive-worker/retention-policy";

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH requested AS (
                UPDATE cirrus.archive_object AS archive_object
                SET archive_status = 'DeletionRequested',
                    deletion_requested_at = {now},
                    deletion_requested_by = {actor}
                FROM cirrus.retention_policy AS policy
                WHERE archive_object.retention_policy_id = policy.retention_policy_id
                  AND archive_object.archive_status = 'Active'
                  AND policy.delete_after_expiry = TRUE
                  AND archive_object.retention_until < {today}
                RETURNING archive_object.archive_object_id,
                          archive_object.tenant_id,
                          archive_object.retention_until
            )
            INSERT INTO cirrus.archive_event
                (tenant_id, archive_object_id, event_type,
                 event_timestamp, actor, details_json)
            SELECT tenant_id, archive_object_id, 'DeletionRequested',
                   {now}, {actor},
                   jsonb_build_object(
                       'reason', 'retention-policy-expired',
                       'retentionUntil', retention_until)
            FROM requested
            """,
            cancellationToken);
    }

    private async Task<WorkItem[]> ClaimBatchAsync(
        string leaseOwner,
        DateTimeOffset now,
        DateOnly today,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext dbContext = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE cirrus.archive_object AS archive_object
            SET purge_lease_owner = {leaseOwner},
                purge_lease_until = {leaseUntil}
            WHERE archive_object.archive_object_id IN (
                SELECT candidate.archive_object_id
                FROM cirrus.archive_object AS candidate
                WHERE candidate.archive_status = 'DeletionRequested'
                  AND candidate.retention_until < {today}
                  AND (candidate.purge_lease_until IS NULL
                       OR candidate.purge_lease_until <= {now})
                  AND NOT EXISTS (
                      SELECT 1
                      FROM cirrus.archive_error_queue AS error
                      WHERE error.archive_object_id = candidate.archive_object_id
                        AND error.error_type = {PurgeErrorType}
                        AND error.resolved = FALSE
                        AND error.next_retry_at > {now})
                ORDER BY candidate.deletion_requested_at,
                         candidate.archive_object_id
                FOR UPDATE SKIP LOCKED
                LIMIT {_options.BatchSize}
            )
            """,
            cancellationToken);

        return await dbContext.ArchiveObjects
            .AsNoTracking()
            .Where(item => item.PurgeLeaseOwner == leaseOwner)
            .Select(item => new WorkItem(item.ArchiveObjectId))
            .ToArrayAsync(cancellationToken);
    }

    private async Task ProcessOneAsync(
        long archiveObjectId,
        string workerInstanceId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            CirrusDbContext dbContext = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
            IObjectStorage storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

            ArchiveObject? item = await dbContext.ArchiveObjects
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.ArchiveObjectId == archiveObjectId
                        && candidate.ArchiveStatus == ArchiveStatus.DeletionRequested
                        && candidate.PurgeLeaseOwner == leaseOwner,
                    cancellationToken);

            if (item is null)
            {
                return;
            }

            DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            if (item.RetentionUntil >= today)
            {
                await ReleaseLeaseAsync(archiveObjectId, leaseOwner, cancellationToken);
                return;
            }

            ObjectStorageDeleteOutcome outcome = await storage.DeleteAsync(
                item.BucketName,
                item.ObjectKey,
                item.StorageVersionId,
                cancellationToken);

            await CompleteAsync(
                archiveObjectId,
                leaseOwner,
                workerInstanceId,
                outcome,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The lease is deliberately retained. After expiry another worker
            // can safely resume; storage deletion itself is idempotent.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Purging archive object {ArchiveObjectId} failed.",
                archiveObjectId);

            try
            {
                await ScheduleRetryAsync(
                    archiveObjectId,
                    leaseOwner,
                    workerInstanceId,
                    exception,
                    cancellationToken);
            }
            catch (Exception retryException)
            {
                logger.LogError(
                    retryException,
                    "Recording the purge retry for archive object {ArchiveObjectId} failed; the lease will expire.",
                    archiveObjectId);
            }
        }
    }

    private async Task CompleteAsync(
        long archiveObjectId,
        string leaseOwner,
        string workerInstanceId,
        ObjectStorageDeleteOutcome outcome,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext dbContext = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        ArchiveObject? item = await dbContext.ArchiveObjects
            .FromSqlInterpolated($"""
                SELECT * FROM cirrus.archive_object
                WHERE archive_object_id = {archiveObjectId}
                  AND archive_status = 'DeletionRequested'
                  AND purge_lease_owner = {leaseOwner}
                FOR UPDATE
                """)
            .Include(candidate => candidate.Errors)
            .SingleOrDefaultAsync(cancellationToken);

        if (item is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        DateTimeOffset purgedAt = timeProvider.GetUtcNow();
        item.ArchiveStatus = ArchiveStatus.Purged;
        item.PurgedAt = purgedAt;
        item.PurgeLeaseOwner = null;
        item.PurgeLeaseUntil = null;
        item.NextIntegrityCheckAt = null;
        item.IntegrityCheckLeaseOwner = null;
        item.IntegrityCheckLeaseUntil = null;

        foreach (ArchiveErrorQueueItem error in item.Errors.Where(error =>
                     error.ErrorType == PurgeErrorType && !error.Resolved))
        {
            error.Resolved = true;
            error.ResolvedAt = purgedAt;
            error.NextRetryAt = null;
        }

        item.Events.Add(new ArchiveEvent
        {
            TenantId = item.TenantId,
            EventType = ArchiveEventType.Purged,
            EventTimestamp = purgedAt,
            Actor = $"archive-worker/{workerInstanceId}",
            DetailsJson = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                storageOutcome = outcome.ToString(),
                objectWasAlreadyMissing = outcome == ObjectStorageDeleteOutcome.NotFound
            }))
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ScheduleRetryAsync(
        long archiveObjectId,
        string leaseOwner,
        string workerInstanceId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext dbContext = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        ArchiveObject? item = await dbContext.ArchiveObjects
            .Include(candidate => candidate.Errors)
            .SingleOrDefaultAsync(candidate =>
                candidate.ArchiveObjectId == archiveObjectId
                && candidate.ArchiveStatus == ArchiveStatus.DeletionRequested
                && candidate.PurgeLeaseOwner == leaseOwner,
                cancellationToken);

        if (item is null)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        ArchiveErrorQueueItem? error = item.Errors
            .Where(candidate => candidate.ErrorType == PurgeErrorType && !candidate.Resolved)
            .OrderByDescending(candidate => candidate.ErrorTimestamp)
            .FirstOrDefault();
        int retryCount = (error?.RetryCount ?? 0) + 1;
        double multiplier = Math.Pow(2, Math.Min(retryCount - 1, 30));
        int delayMinutes = (int)Math.Min(
            _options.MaximumRetryDelayMinutes,
            _options.InitialRetryDelayMinutes * multiplier);
        DateTimeOffset retryAt = now.AddMinutes(delayMinutes);
        string safeMessage = exception.Message.Length <= 4000
            ? exception.Message
            : exception.Message[..4000];

        if (error is null)
        {
            error = new ArchiveErrorQueueItem
            {
                ErrorType = PurgeErrorType,
                ErrorTimestamp = now,
                RetryCount = retryCount,
                LastErrorMessage = safeMessage,
                NextRetryAt = retryAt,
                Resolved = false
            };
            item.Errors.Add(error);
        }
        else
        {
            error.ErrorTimestamp = now;
            error.RetryCount = retryCount;
            error.LastErrorMessage = safeMessage;
            error.NextRetryAt = retryAt;
        }

        item.PurgeLeaseOwner = null;
        item.PurgeLeaseUntil = null;
        item.Events.Add(new ArchiveEvent
        {
            TenantId = item.TenantId,
            EventType = ArchiveEventType.PurgeFailed,
            EventTimestamp = now,
            Actor = $"archive-worker/{workerInstanceId}",
            DetailsJson = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                retryCount,
                retryAt,
                errorType = exception.GetType().Name
            }))
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseLeaseAsync(
        long archiveObjectId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext dbContext = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        await dbContext.ArchiveObjects
            .Where(item => item.ArchiveObjectId == archiveObjectId
                && item.PurgeLeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.PurgeLeaseOwner, (string?)null)
                .SetProperty(item => item.PurgeLeaseUntil, (DateTimeOffset?)null),
                cancellationToken);
    }
}
