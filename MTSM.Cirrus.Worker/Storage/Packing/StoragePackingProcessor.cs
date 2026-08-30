using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Worker.StorageV2;

namespace MTSM.Cirrus.Worker;

public sealed class StoragePackingProcessor(
    IServiceScopeFactory scopeFactory,
    StoragePackingLeaseManager leases,
    ArchivePackPlanner planner,
    StagingFinalizer finalizer,
    ILogger<StoragePackingProcessor> logger)
{
    public async Task<int> ProcessBatchAsync(string workerId, CancellationToken cancellationToken)
    {
        string lease = $"{workerId}/{Guid.NewGuid():N}";
        StoragePackingWorkItem[] cleanup = await leases.ClaimCleanupAsync(lease, cancellationToken);
        foreach (StoragePackingWorkItem item in cleanup)
        {
            try
            {
                await finalizer.VerifyAndCleanupAsync(item.ArchiveObjectId, workerId, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Cleaning staged archive object {ArchiveObjectId} failed.", item.ArchiveObjectId);
                await leases.ReleaseCleanupAsync(item.ArchiveObjectId, lease, exception, cancellationToken);
            }
        }

        StoragePackingWorkItem[] claimed = await leases.ClaimPackingAsync(lease, cancellationToken);
        if (claimed.Length == 0) return cleanup.Length;
        long[] ids = claimed.Select(x => x.ArchiveObjectId).ToArray();
        await using IAsyncDisposable heartbeat = leases.StartHeartbeat(ids, lease, cancellationToken);
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        ArchiveObject[] items = await db.ArchiveObjects.AsNoTracking().Include(x => x.Tenant)
            .Where(x => ids.Contains(x.ArchiveObjectId)).ToArrayAsync(cancellationToken);

        foreach (IGrouping<(long TenantId, string Bucket), ArchiveObject> group in
                 items.GroupBy(x => (x.TenantId, x.BucketName)))
        {
            ArchiveObject[] groupItems = group.ToArray();
            try
            {
                await planner.PackAndCommitAsync(groupItems, lease, cancellationToken);
                foreach (ArchiveObject item in groupItems)
                    await finalizer.VerifyAndCleanupAsync(item.ArchiveObjectId, workerId, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Packing {Count} archive objects failed.", groupItems.Length);
                await leases.ReleasePackingAsync(groupItems.Select(x => x.ArchiveObjectId).ToArray(),
                    lease, exception, cancellationToken);
            }
        }
        return cleanup.Length + claimed.Length;
    }
}
