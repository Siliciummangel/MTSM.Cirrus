using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Worker.StorageV2;

namespace MTSM.Cirrus.Worker;

public sealed class StoragePackingLeaseManager(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<StoragePackingLeaseManager> logger)
{
    private readonly StorageProcessingOptions _options = options.Value;

    internal async Task<StoragePackingWorkItem[]> ClaimCleanupAsync(string lease, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset leaseUntil = now.AddMinutes(_options.LeaseDurationMinutes);
        return await db.Database.SqlQuery<StoragePackingWorkItem>($"""
            WITH candidates AS MATERIALIZED (
              SELECT archive_object_id FROM cirrus.archive_object
              WHERE content_manifest_id IS NOT NULL AND staging_object_key IS NOT NULL
                AND (storage_processing_status = 'CleanupPending'
                  OR (storage_processing_status = 'Cleaning' AND storage_processing_lease_until <= {now}))
              ORDER BY archive_object_id FOR UPDATE SKIP LOCKED LIMIT {_options.BatchSize}
            ), claimed AS (
              UPDATE cirrus.archive_object a SET storage_processing_status = 'Cleaning',
                storage_processing_lease_owner = {lease}, storage_processing_lease_until = {leaseUntil}
              FROM candidates WHERE a.archive_object_id = candidates.archive_object_id
              RETURNING a.archive_object_id
            ) SELECT archive_object_id FROM claimed
            """).ToArrayAsync(cancellationToken);
    }

    internal async Task<StoragePackingWorkItem[]> ClaimPackingAsync(string lease, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset oldEnough = now.AddSeconds(-_options.MaximumBatchWaitSeconds);
        DateTimeOffset leaseUntil = now.AddMinutes(_options.LeaseDurationMinutes);
        return await db.Database.SqlQuery<StoragePackingWorkItem>($"""
            WITH stats AS (
              SELECT COUNT(*) AS count, MIN(storage_processing_verified_at) AS oldest
              FROM cirrus.archive_object WHERE storage_processing_status = 'Ready'
                AND (storage_processing_next_attempt_at IS NULL OR storage_processing_next_attempt_at <= {now})
            ), candidates AS MATERIALIZED (
              SELECT a.archive_object_id FROM cirrus.archive_object a CROSS JOIN stats
              WHERE a.staging_object_key IS NOT NULL AND a.archive_status = 'Active'
                AND ((a.storage_processing_status = 'Ready'
                    AND (a.storage_processing_next_attempt_at IS NULL OR a.storage_processing_next_attempt_at <= {now}))
                  OR (a.storage_processing_status = 'Packing' AND a.storage_processing_lease_until <= {now}))
                AND (stats.count >= {_options.BatchSize} OR stats.oldest <= {oldEnough}
                  OR a.size_bytes >= {_options.TargetPackSizeBytes})
              ORDER BY a.storage_processing_verified_at, a.archive_object_id
              FOR UPDATE OF a SKIP LOCKED LIMIT {_options.BatchSize}
            ), claimed AS (
              UPDATE cirrus.archive_object a SET storage_processing_status = 'Packing',
                storage_processing_lease_owner = {lease}, storage_processing_lease_until = {leaseUntil}
              FROM candidates WHERE a.archive_object_id = candidates.archive_object_id
              RETURNING a.archive_object_id
            ) SELECT archive_object_id FROM claimed
            """).ToArrayAsync(cancellationToken);
    }

    public async Task ReleasePackingAsync(long[] ids, string lease, Exception exception, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        DateTimeOffset retryAt = timeProvider.GetUtcNow().AddSeconds(_options.InitialRetryDelaySeconds);
        await db.ArchiveObjects.Where(x => ids.Contains(x.ArchiveObjectId) && x.StorageProcessingLeaseOwner == lease)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.StorageProcessingStatus, StorageProcessingStatus.Ready)
                .SetProperty(x => x.StorageProcessingLeaseOwner, (string?)null)
                .SetProperty(x => x.StorageProcessingLeaseUntil, (DateTimeOffset?)null)
                .SetProperty(x => x.StorageProcessingNextAttemptAt, retryAt)
                .SetProperty(x => x.StorageProcessingErrorCode, "STORAGE_PACKING_FAILED")
                .SetProperty(x => x.StorageProcessingErrorMessage,
                    $"Storage packing failed with {exception.GetType().Name}."), cancellationToken);
    }

    public async Task ReleaseCleanupAsync(long archiveId, string lease, Exception exception, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        await db.ArchiveObjects.Where(x => x.ArchiveObjectId == archiveId && x.StorageProcessingLeaseOwner == lease)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.StorageProcessingStatus, StorageProcessingStatus.CleanupPending)
                .SetProperty(x => x.StorageProcessingLeaseOwner, (string?)null)
                .SetProperty(x => x.StorageProcessingLeaseUntil, (DateTimeOffset?)null)
                .SetProperty(x => x.StorageProcessingErrorCode, "STAGING_CLEANUP_FAILED")
                .SetProperty(x => x.StorageProcessingErrorMessage,
                    $"Staging cleanup failed with {exception.GetType().Name}."), cancellationToken);
    }

    public IAsyncDisposable StartHeartbeat(long[] ids, string lease, CancellationToken cancellationToken) =>
        new PackingLeaseHeartbeat(scopeFactory, ids, lease, _options, timeProvider, logger, cancellationToken);

    private sealed class PackingLeaseHeartbeat : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _loop;
        public PackingLeaseHeartbeat(IServiceScopeFactory scopeFactory, long[] ids, string lease,
            StorageProcessingOptions options, TimeProvider timeProvider, ILogger logger, CancellationToken token)
        {
            _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            _loop = RunAsync(scopeFactory, ids, lease, options, timeProvider, logger, _stop.Token);
        }
        private static async Task RunAsync(IServiceScopeFactory scopeFactory, long[] ids, string lease,
            StorageProcessingOptions options, TimeProvider timeProvider, ILogger logger, CancellationToken token)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.LeaseHeartbeatSeconds), timeProvider);
                while (await timer.WaitForNextTickAsync(token))
                {
                    await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                    CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
                    DateTimeOffset until = timeProvider.GetUtcNow().AddMinutes(options.LeaseDurationMinutes);
                    int renewed = await db.ArchiveObjects.Where(x => ids.Contains(x.ArchiveObjectId)
                            && x.StorageProcessingStatus == StorageProcessingStatus.Packing
                            && x.StorageProcessingLeaseOwner == lease)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.StorageProcessingLeaseUntil, until), token);
                    if (renewed == 0) return;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Packing lease heartbeat failed for {Lease}.", lease); }
        }
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try { await _loop; } catch (OperationCanceledException) { }
            _stop.Dispose();
        }
    }
}
