using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Worker.Maintenance;

public sealed class PackGarbageCollector(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<PackGarbageCollector> logger)
{
    private readonly StorageProcessingOptions _options = options.Value;

    public async Task<int> CollectAsync(string lease, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        IObjectStorage storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset orphanCutoff = now.AddMinutes(-_options.OrphanGracePeriodMinutes);
        DateTimeOffset leaseUntil = now.AddMinutes(_options.LeaseDurationMinutes);
        PackMaintenanceWork[] work = await db.Database.SqlQuery<PackMaintenanceWork>($"""
            WITH candidates AS MATERIALIZED (
              SELECT p.storage_pack_id FROM cirrus.storage_pack p
              WHERE (p.pack_status = 'GarbagePending'
                    OR (p.pack_status IN ('Orphaned', 'Uploaded') AND p.created_at <= {orphanCutoff}))
                AND (p.maintenance_lease_until IS NULL OR p.maintenance_lease_until <= {now})
                AND NOT EXISTS (SELECT 1 FROM cirrus.storage_location l
                                WHERE l.storage_pack_id = p.storage_pack_id)
              ORDER BY p.created_at FOR UPDATE SKIP LOCKED LIMIT {_options.PackMaintenanceBatchSize}
            ), claimed AS (
              UPDATE cirrus.storage_pack p SET maintenance_lease_owner = {lease},
                maintenance_lease_until = {leaseUntil}, maintenance_attempts = maintenance_attempts + 1
              FROM candidates WHERE p.storage_pack_id = candidates.storage_pack_id
              RETURNING p.storage_pack_id
            ) SELECT storage_pack_id FROM claimed
            """).ToArrayAsync(cancellationToken);

        int completed = 0;
        foreach (PackMaintenanceWork item in work)
        {
            try
            {
                StoragePack pack = await db.StoragePacks.SingleAsync(x => x.StoragePackId == item.StoragePackId
                    && x.MaintenanceLeaseOwner == lease, cancellationToken);
                await storage.DeleteAsync(pack.BucketName, pack.ObjectKey, pack.StorageVersionId, cancellationToken);
                db.StoragePacks.Remove(pack);
                await db.SaveChangesAsync(cancellationToken);
                completed++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Garbage collection failed for pack {StoragePackId}.", item.StoragePackId);
                await db.StoragePacks.Where(x => x.StoragePackId == item.StoragePackId
                        && x.MaintenanceLeaseOwner == lease)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.MaintenanceLeaseOwner, (string?)null)
                        .SetProperty(x => x.MaintenanceLeaseUntil, (DateTimeOffset?)null)
                        .SetProperty(x => x.MaintenanceError, $"GC failed with {exception.GetType().Name}."), cancellationToken);
                db.ChangeTracker.Clear();
            }
        }
        return completed;
    }
}
