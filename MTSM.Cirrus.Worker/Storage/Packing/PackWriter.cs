using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Worker.StorageV2;

namespace MTSM.Cirrus.Worker;

public sealed class PackWriter(TimeProvider timeProvider)
{
    internal async Task<UploadedPack> UploadAsync(IObjectStorage storage, CirrusDbContext db,
        ArchiveObject context, TemporaryPackBuilder builder, IReadOnlyList<PendingPackChunk> pending,
        CancellationToken cancellationToken)
    {
        await using (builder)
        {
            SealedPack sealedPack = await builder.SealAsync(cancellationToken);
            DateTimeOffset at = timeProvider.GetUtcNow();
            string key = string.Join('/', context.Tenant.ObjectKeyPrefix.Trim('/'), "packs", "v1",
                at.UtcDateTime.ToString("yyyy/MM/dd"), Guid.NewGuid().ToString("N"));
            ObjectStorageWriteResult write = await storage.WriteAsync(context.BucketName, key,
                sealedPack.Content, "application/vnd.mtsm.cirrus.pack", null, cancellationToken);
            var pack = new StoragePack
            {
                TenantId = context.TenantId, BucketName = context.BucketName, ObjectKey = key,
                StorageVersionId = write.VersionId ?? write.ETag, PackFormatVersion = 1,
                HashAlgorithm = "SHA-256", PackHash = sealedPack.Sha256Hash, SizeBytes = sealedPack.Length,
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
