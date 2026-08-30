using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using System.Security.Cryptography;

namespace MTSM.Cirrus.Worker;

public sealed class StagingFinalizer(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
{
    public async Task VerifyAndCleanupAsync(long archiveId, string workerId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        IManifestContentReader reader = scope.ServiceProvider.GetRequiredService<IManifestContentReader>();
        IObjectStorage storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        ArchiveObject item = await db.ArchiveObjects.SingleAsync(x => x.ArchiveObjectId == archiveId, cancellationToken);
        await using Stream restored = await reader.OpenReadAsync(item.ContentManifestId!.Value, cancellationToken);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        long size = 0;
        while (true)
        {
            int read = await restored.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            size += read;
        }
        string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (size != item.SizeBytes || !string.Equals(actual, item.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Restored archive object {archiveId} failed verification.");
        await storage.DeleteAsync(item.BucketName, item.StagingObjectKey!, item.StorageVersionId, cancellationToken);
        item.StagingObjectKey = null;
        item.StorageVersionId = null;
        item.StorageProcessingStatus = StorageProcessingStatus.Completed;
        item.StorageProcessingCompletedAt = timeProvider.GetUtcNow();
        item.StorageProcessingLeaseOwner = null;
        item.StorageProcessingLeaseUntil = null;
        item.Events.Add(new ArchiveEvent { TenantId = item.TenantId,
            EventType = ArchiveEventType.StorageProcessingCompleted,
            EventTimestamp = item.StorageProcessingCompletedAt.Value, Actor = $"archive-worker/{workerId}" });
        await db.SaveChangesAsync(cancellationToken);
    }
}
