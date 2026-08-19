using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Models;
using System.Text.Json;

namespace MTSM.Cirrus.Worker;

public sealed class IntegrityCheckProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<IntegrityCheckOptions> options,
    ILogger<IntegrityCheckProcessor> logger)
{
    private sealed record WorkItem(long ArchiveObjectId, long TenantId);

    private readonly IntegrityCheckOptions _options = options.Value;

    public async Task<int> ProcessBatchAsync(
        string workerInstanceId,
        CancellationToken cancellationToken)
    {
        string leaseOwner =
            $"{workerInstanceId}/{Guid.NewGuid():N}";

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset leaseUntil =
            now.AddMinutes(_options.LeaseDurationMinutes);

        await InitializeSchedulesAsync(
            cancellationToken);

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
            "Worker {WorkerInstanceId} claimed {Count} integrity checks.",
            workerInstanceId,
            workItems.Length);

        await Parallel.ForEachAsync(
            workItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism =
                    _options.MaxConcurrentChecks,
                CancellationToken = cancellationToken
            },
            async (workItem, itemCancellationToken) =>
                await ProcessOneAsync(
                    workItem.TenantId,
                    workItem.ArchiveObjectId,
                    workerInstanceId,
                    leaseOwner,
                    itemCancellationToken));

        return workItems.Length;
    }

    private async Task InitializeSchedulesAsync(
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope =
            scopeFactory.CreateAsyncScope();

        CirrusDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<CirrusDbContext>();

        TimeSpan initialDelay =
            TimeSpan.FromHours(
                _options.InitialVerificationDelayHours);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE cirrus.archive_object
            SET next_integrity_check_at = archived_at + {initialDelay}
            WHERE archive_status = 'Active'
              AND archived_at IS NOT NULL
              AND next_integrity_check_at IS NULL
            """,
            cancellationToken);
    }

    private async Task<WorkItem[]> ClaimBatchAsync(
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope =
            scopeFactory.CreateAsyncScope();

        CirrusDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<CirrusDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE cirrus.archive_object AS archive_object
            SET integrity_check_lease_owner = {leaseOwner},
                integrity_check_lease_until = {leaseUntil}
            WHERE archive_object.archive_object_id IN (
                SELECT candidate.archive_object_id
                FROM cirrus.archive_object AS candidate
                INNER JOIN cirrus.tenant AS tenant
                    ON tenant.tenant_id = candidate.tenant_id
                WHERE candidate.archive_status = 'Active'
                  AND tenant.status <> 'Disabled'
                  AND candidate.archived_at IS NOT NULL
                  AND candidate.next_integrity_check_at <= {now}
                  AND (
                        candidate.integrity_check_lease_until IS NULL
                        OR candidate.integrity_check_lease_until <= {now})
                ORDER BY candidate.next_integrity_check_at,
                    candidate.archive_object_id
                FOR UPDATE SKIP LOCKED
                LIMIT {_options.BatchSize}
            )
            """,
            cancellationToken);

        return await dbContext.ArchiveObjects
            .AsNoTracking()
            .Where(archiveObject =>
                archiveObject.IntegrityCheckLeaseOwner == leaseOwner)
            .OrderBy(archiveObject => archiveObject.ArchiveObjectId)
            .Select(archiveObject => new WorkItem(
                archiveObject.ArchiveObjectId,
                archiveObject.TenantId))
            .ToArrayAsync(cancellationToken);
    }

    private async Task ProcessOneAsync(
        long tenantId,
        long archiveObjectId,
        string workerInstanceId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        using var leaseCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        Task leaseHeartbeat = RenewLeaseAsync(
            archiveObjectId,
            leaseOwner,
            leaseCancellation,
            cancellationToken);

        try
        {
            await using AsyncServiceScope scope =
                scopeFactory.CreateAsyncScope();

            IArchiveService archiveService =
                scope.ServiceProvider
                    .GetRequiredService<IArchiveService>();

            ArchiveIntegrityResult result =
                await archiveService.VerifyIntegrityAsync(
                    tenantId,
                    archiveObjectId,
                    $"archive-worker/{workerInstanceId}",
                    leaseCancellation.Token);

            CirrusDbContext dbContext =
                scope.ServiceProvider
                    .GetRequiredService<CirrusDbContext>();

            DateTimeOffset nextCheckAt =
                result.VerifiedAt.AddDays(
                    _options.ReverificationIntervalDays);

            int updated = await dbContext.ArchiveObjects
                .Where(archiveObject =>
                    archiveObject.ArchiveObjectId == archiveObjectId
                    && archiveObject.IntegrityCheckLeaseOwner == leaseOwner)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            archiveObject =>
                                archiveObject.LastIntegrityCheckAt,
                            result.VerifiedAt)
                        .SetProperty(
                            archiveObject =>
                                archiveObject.NextIntegrityCheckAt,
                            nextCheckAt)
                        .SetProperty(
                            archiveObject =>
                                archiveObject.IntegrityCheckLeaseOwner,
                            (string?)null)
                        .SetProperty(
                            archiveObject =>
                                archiveObject.IntegrityCheckLeaseUntil,
                            (DateTimeOffset?)null),
                    cancellationToken);

            if (updated == 0)
            {
                logger.LogWarning(
                    "Worker {WorkerInstanceId} completed integrity check " +
                    "for archive object {ArchiveObjectId}, but no longer owns its lease.",
                    workerInstanceId,
                    archiveObjectId);
            }
            else
            {
                await dbContext.ArchiveErrorQueue
                    .Where(error =>
                        error.ArchiveObjectId == archiveObjectId
                        && error.ErrorType == "INTEGRITY_CHECK_FAILED"
                        && !error.Resolved)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                error => error.Resolved,
                                true)
                            .SetProperty(
                                error => error.ResolvedAt,
                                result.VerifiedAt)
                            .SetProperty(
                                error => error.NextRetryAt,
                                (DateTimeOffset?)null),
                        cancellationToken);

                logger.LogInformation(
                    "Worker {WorkerInstanceId} completed integrity check " +
                    "for archive object {ArchiveObjectId} with result {IsValid}. " +
                    "Next check is scheduled for {NextCheckAt}.",
                    workerInstanceId,
                    archiveObjectId,
                    result.IsValid,
                    nextCheckAt);
            }
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
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Worker {WorkerInstanceId} failed to verify archive object " +
                "{ArchiveObjectId}.",
                workerInstanceId,
                archiveObjectId);

            try
            {
                await ScheduleRetryAsync(
                    archiveObjectId,
                    leaseOwner,
                    exception,
                    cancellationToken);
            }
            catch (Exception schedulingException)
            {
                logger.LogError(
                    schedulingException,
                    "Scheduling a retry for archive object " +
                    "{ArchiveObjectId} failed. Its lease will expire at " +
                    "the configured deadline.",
                    archiveObjectId);
            }
        }
        finally
        {
            await leaseCancellation.CancelAsync();

            try
            {
                await leaseHeartbeat;
            }
            catch (OperationCanceledException)
            {
                // Expected when the verification completes or shuts down.
            }
        }
    }

    private async Task RenewLeaseAsync(
        long archiveObjectId,
        string leaseOwner,
        CancellationTokenSource leaseCancellation,
        CancellationToken shutdownToken)
    {
        TimeSpan renewalInterval = TimeSpan.FromMinutes(
            Math.Max(1, _options.LeaseDurationMinutes / 3d));

        try
        {
            using var timer = new PeriodicTimer(renewalInterval);

            while (await timer.WaitForNextTickAsync(
                leaseCancellation.Token))
            {
                await using AsyncServiceScope scope =
                    scopeFactory.CreateAsyncScope();

                CirrusDbContext dbContext =
                    scope.ServiceProvider
                        .GetRequiredService<CirrusDbContext>();

                DateTimeOffset leaseUntil =
                    DateTimeOffset.UtcNow.AddMinutes(
                        _options.LeaseDurationMinutes);

                int updated = await dbContext.ArchiveObjects
                    .Where(archiveObject =>
                        archiveObject.ArchiveObjectId == archiveObjectId
                        && archiveObject.IntegrityCheckLeaseOwner == leaseOwner)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            archiveObject =>
                                archiveObject.IntegrityCheckLeaseUntil,
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
            // Expected when processing completes, the lease is lost or shutdown starts.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Renewing integrity-check lease {LeaseOwner} for archive " +
                "object {ArchiveObjectId} failed.",
                leaseOwner,
                archiveObjectId);

            await leaseCancellation.CancelAsync();
        }
    }

    private async Task ScheduleRetryAsync(
        long archiveObjectId,
        string leaseOwner,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope =
            scopeFactory.CreateAsyncScope();

        CirrusDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<CirrusDbContext>();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset retryAt =
            now.AddMinutes(_options.FailureRetryDelayMinutes);

        ArchiveObject? archiveObject =
            await dbContext.ArchiveObjects
                .Include(item => item.Errors)
                .SingleOrDefaultAsync(
                    item =>
                        item.ArchiveObjectId == archiveObjectId
                        && item.IntegrityCheckLeaseOwner == leaseOwner,
                    cancellationToken);

        if (archiveObject is null)
        {
            return;
        }

        archiveObject.NextIntegrityCheckAt = retryAt;
        archiveObject.IntegrityCheckLeaseOwner = null;
        archiveObject.IntegrityCheckLeaseUntil = null;

        ArchiveErrorQueueItem? retryError =
            archiveObject.Errors
                .Where(error =>
                    error.ErrorType == "INTEGRITY_CHECK_FAILED"
                    && !error.Resolved)
                .OrderByDescending(error => error.ErrorTimestamp)
                .FirstOrDefault();

        if (retryError is null)
        {
            archiveObject.Errors.Add(new ArchiveErrorQueueItem
            {
                ErrorType = "INTEGRITY_CHECK_FAILED",
                ErrorTimestamp = now,
                RetryCount = 1,
                LastErrorMessage = exception.Message,
                NextRetryAt = retryAt,
                Resolved = false
            });
        }
        else
        {
            retryError.ErrorTimestamp = now;
            retryError.RetryCount++;
            retryError.LastErrorMessage = exception.Message;
            retryError.NextRetryAt = retryAt;
        }

        archiveObject.Events.Add(new ArchiveEvent
        {
            EventType = ArchiveEventType.ErrorOccurred,
            EventTimestamp = now,
            Actor = $"archive-worker/{leaseOwner}",
            DetailsJson = JsonDocument.Parse(
                JsonSerializer.Serialize(new
                {
                    errorType = "INTEGRITY_CHECK_FAILED",
                    retryAt
                }))
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseLeaseAsync(
        long archiveObjectId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope =
                scopeFactory.CreateAsyncScope();

            CirrusDbContext dbContext =
                scope.ServiceProvider
                    .GetRequiredService<CirrusDbContext>();

            await dbContext.ArchiveObjects
                .Where(archiveObject =>
                    archiveObject.ArchiveObjectId == archiveObjectId
                    && archiveObject.IntegrityCheckLeaseOwner == leaseOwner)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            archiveObject =>
                                archiveObject.IntegrityCheckLeaseOwner,
                            (string?)null)
                        .SetProperty(
                            archiveObject =>
                                archiveObject.IntegrityCheckLeaseUntil,
                            (DateTimeOffset?)null),
                    cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Releasing integrity-check lease {LeaseOwner} for archive " +
                "object {ArchiveObjectId} during shutdown failed. The lease " +
                "will expire at its configured deadline.",
                leaseOwner,
                archiveObjectId);
        }
    }
}
