using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Worker.Maintenance;

public sealed class PackMaintenanceProcessor(
    IServiceScopeFactory scopeFactory,
    UnreachableContentCollector unreachableContent,
    PackGarbageCollector garbageCollector,
    PackMaintenanceLeaseManager leases,
    PackCompactor compactor,
    ILogger<PackMaintenanceProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(string workerId, CancellationToken cancellationToken)
    {
        string lease = $"{workerId}/pack-maintenance/{Guid.NewGuid():N}";
        await unreachableContent.PruneAsync(cancellationToken);
        int collected = await garbageCollector.CollectAsync(lease, cancellationToken);
        long[] claimed = await leases.ClaimCompactionAsync(lease, cancellationToken);
        if (claimed.Length == 0) return collected;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        StoragePack[] packs = await db.StoragePacks.AsNoTracking()
            .Where(x => claimed.Contains(x.StoragePackId) && x.MaintenanceLeaseOwner == lease)
            .ToArrayAsync(cancellationToken);

        foreach (IGrouping<(long TenantId, string BucketName), StoragePack> group in
                 packs.GroupBy(x => (x.TenantId, x.BucketName)))
        {
            StoragePack[] groupPacks = group.ToArray();
            try
            {
                await using IAsyncDisposable heartbeat = leases.StartHeartbeat(
                    groupPacks.Select(x => x.StoragePackId).ToArray(), lease, cancellationToken);
                await compactor.CompactAsync(groupPacks, lease, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Pack compaction failed for {Count} packs.", groupPacks.Length);
                await leases.ReleaseAsync(groupPacks.Select(x => x.StoragePackId).ToArray(),
                    lease, exception, cancellationToken);
            }
        }
        return collected + claimed.Length;
    }
}
