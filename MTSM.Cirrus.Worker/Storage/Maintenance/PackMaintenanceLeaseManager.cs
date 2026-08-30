using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Enums;

namespace MTSM.Cirrus.Worker.Maintenance;

public sealed class PackMaintenanceLeaseManager(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<PackMaintenanceLeaseManager> logger)
{
    private readonly StorageProcessingOptions _options = options.Value;

    internal async Task<long[]> ClaimCompactionAsync(string lease, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset cutoff = now.AddMinutes(-_options.CompactionMinimumAgeMinutes);
        DateTimeOffset leaseUntil = now.AddMinutes(_options.LeaseDurationMinutes);
        PackMaintenanceWork[] claimed = await db.Database.SqlQuery<PackMaintenanceWork>($"""
            WITH candidates AS MATERIALIZED (
              SELECT p.storage_pack_id FROM cirrus.storage_pack p
              WHERE p.pack_status = 'Committed' AND p.created_at <= {cutoff}
                AND (p.maintenance_lease_until IS NULL OR p.maintenance_lease_until <= {now})
                AND EXISTS (SELECT 1 FROM cirrus.storage_location l WHERE l.storage_pack_id = p.storage_pack_id)
                AND (SELECT COALESCE(SUM(l.stored_length), 0) FROM cirrus.storage_location l
                     WHERE l.storage_pack_id = p.storage_pack_id) * 100
                    < p.size_bytes * {_options.CompactionUtilizationPercent}
              ORDER BY p.created_at FOR UPDATE SKIP LOCKED LIMIT {_options.PackMaintenanceBatchSize}
            ), claimed AS (
              UPDATE cirrus.storage_pack p SET maintenance_lease_owner = {lease},
                maintenance_lease_until = {leaseUntil}, maintenance_attempts = maintenance_attempts + 1
              FROM candidates WHERE p.storage_pack_id = candidates.storage_pack_id
              RETURNING p.storage_pack_id
            ) SELECT storage_pack_id FROM claimed
            """).ToArrayAsync(cancellationToken);
        return claimed.Select(x => x.StoragePackId).ToArray();
    }

    public async Task ReleaseAsync(long[] ids, string lease, Exception exception, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        await db.StoragePacks.Where(x => ids.Contains(x.StoragePackId) && x.MaintenanceLeaseOwner == lease)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.MaintenanceLeaseOwner, (string?)null)
                .SetProperty(x => x.MaintenanceLeaseUntil, (DateTimeOffset?)null)
                .SetProperty(x => x.MaintenanceError, $"Compaction failed with {exception.GetType().Name}."), cancellationToken);
    }

    public IAsyncDisposable StartHeartbeat(long[] ids, string lease, CancellationToken cancellationToken) =>
        new PackLeaseHeartbeat(scopeFactory, ids, lease, _options, timeProvider, logger, cancellationToken);

    private sealed class PackLeaseHeartbeat : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _loop;
        public PackLeaseHeartbeat(IServiceScopeFactory scopeFactory, long[] ids, string lease,
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
                    int renewed = await db.StoragePacks.Where(x => ids.Contains(x.StoragePackId)
                            && x.PackStatus == PackStatus.Committed && x.MaintenanceLeaseOwner == lease)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.MaintenanceLeaseUntil, until), token);
                    if (renewed == 0) return;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Pack-maintenance heartbeat failed."); }
        }
        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try { await _loop; } catch (OperationCanceledException) { }
            _stop.Dispose();
        }
    }
}
