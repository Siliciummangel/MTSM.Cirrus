using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Worker.StorageV2;

namespace MTSM.Cirrus.Worker;

public sealed class PackWriter(TimeProvider timeProvider)
{
    internal string CreateObjectKey(ArchiveObject context) => string.Join('/',
        context.Tenant.ObjectKeyPrefix.Trim('/'), "packs", "v1",
        timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy/MM/dd"), Guid.NewGuid().ToString("N"));

    internal async Task<UploadedPack> UploadAsync(CirrusDbContext db,
        ArchiveObject context, string key, StreamingPackWriter builder, IReadOnlyList<PendingPackChunk> pending,
        CancellationToken cancellationToken)
    {
        await using (builder)
        {
            UploadedPackContent uploaded = await builder.CompleteAsync(cancellationToken);
            DateTimeOffset at = timeProvider.GetUtcNow();
            ObjectStorageWriteResult write = uploaded.Write;
            var pack = new StoragePack
            {
                TenantId = context.TenantId, BucketName = context.BucketName, ObjectKey = key,
                StorageVersionId = write.VersionId ?? write.ETag, PackFormatVersion = 1,
                HashAlgorithm = "SHA-256", PackHash = uploaded.Sha256Hash, SizeBytes = uploaded.Length,
                PackStatus = PackStatus.Uploaded, CreatedAt = at, UploadedAt = at
            };
            db.StoragePacks.Add(pack);
            await db.SaveChangesAsync(cancellationToken);
            PackChunkCandidate[] candidates = pending.Select(entry =>
                new PackChunkCandidate(entry.Hash, entry.Length, pack.StoragePackId, entry.Entry)).ToArray();
            return new UploadedPack(pack.StoragePackId, candidates);
        }
    }
}
