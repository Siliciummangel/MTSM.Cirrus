using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Enums;

namespace MTSM.Cirrus.Worker.Maintenance;

public sealed class UnreachableContentCollector(IServiceScopeFactory scopeFactory)
{
    public async Task PruneAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(cancellationToken);
            await db.ContentManifests
                .Where(x => !db.ArchiveObjects.Any(a => a.ContentManifestId == x.ContentManifestId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.StorageLocations
                .Where(x => !db.ManifestChunks.Any(m => m.ContentChunkId == x.ContentChunkId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.ContentChunks
                .Where(x => !db.ManifestChunks.Any(m => m.ContentChunkId == x.ContentChunkId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.StoragePacks
                .Where(x => x.PackStatus == PackStatus.Committed && !x.StorageLocations.Any())
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PackStatus, PackStatus.GarbagePending), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
