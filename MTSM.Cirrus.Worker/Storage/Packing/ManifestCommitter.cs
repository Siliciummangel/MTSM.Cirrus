using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Worker.StorageV2;
using System.Data.Common;

namespace MTSM.Cirrus.Worker;

public sealed class ManifestCommitter(TimeProvider timeProvider)
{
    internal async Task CommitAsync(CirrusDbContext db, ArchiveObject[] items,
        List<ArchivePackPlan> plans, Dictionary<string, PackChunkCandidate> candidates,
        List<long> packIds, ChunkingProfile profile, string lease, CancellationToken cancellationToken)
    {
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var registered = new Dictionary<string, RegisteredChunk>(StringComparer.OrdinalIgnoreCase);
            var usedPacks = new HashSet<long>();
            foreach (PackChunkCandidate candidate in candidates.Values)
            {
                RegisteredChunk result = await RegisterChunkAsync(db, items[0].TenantId, candidate, cancellationToken);
                registered[candidate.Hash] = result;
                if (result.Inserted)
                {
                    db.StorageLocations.Add(new StorageLocation
                    {
                        ContentChunkId = result.Id,
                        StoragePackId = candidate.PackId,
                        PackOffset = candidate.Entry.Offset,
                        StoredLength = candidate.Entry.StoredLength,
                        RawLength = candidate.Length,
                        CompressionAlgorithm = "Zstd",
                        CompressionVersion = 1,
                        StorageFormatVersion = 1,
                        CreatedAt = timeProvider.GetUtcNow()
                    });
                    usedPacks.Add(candidate.PackId);
                }
            }
            foreach (ArchivePackPlan plan in plans)
            {
                ArchiveObject item = await db.ArchiveObjects.SingleAsync(x => x.ArchiveObjectId == plan.ArchiveObjectId
                    && x.StorageProcessingStatus == StorageProcessingStatus.Packing
                    && x.StorageProcessingLeaseOwner == lease, cancellationToken);
                var manifest = new ContentManifest
                {
                    TenantId = item.TenantId,
                    ManifestFormatVersion = 1,
                    HashAlgorithm = "SHA-256",
                    OriginalHash = item.Sha256Hash!,
                    OriginalSizeBytes = item.SizeBytes,
                    ChunkingAlgorithm = profile.Algorithm,
                    ChunkingAlgorithmVersion = profile.AlgorithmVersion,
                    MinimumChunkSizeBytes = profile.MinimumSizeBytes,
                    AverageChunkSizeBytes = profile.AverageSizeBytes,
                    MaximumChunkSizeBytes = profile.MaximumSizeBytes,
                    ChunkCount = plan.Chunks.Count,
                    CommittedAt = timeProvider.GetUtcNow()
                };
                foreach (PlannedChunk chunk in plan.Chunks)
                    manifest.Chunks.Add(new ManifestChunk
                    {
                        SequenceNumber = chunk.Sequence,
                        OriginalOffset = chunk.Offset,
                        RawLength = chunk.Length,
                        ContentChunkId = chunk.ExistingId ?? registered[chunk.Hash].Id
                    });
                db.ContentManifests.Add(manifest);
                item.ContentManifest = manifest;
                item.StorageProcessingStatus = StorageProcessingStatus.CleanupPending;
                item.StorageProcessingNextAttemptAt = null;
                item.StorageProcessingLeaseOwner = null;
                item.StorageProcessingLeaseUntil = null;
            }
            StoragePack[] packs = await db.StoragePacks.Where(x => packIds.Contains(x.StoragePackId)).ToArrayAsync(cancellationToken);
            foreach (StoragePack pack in packs)
            {
                pack.PackStatus = usedPacks.Contains(pack.StoragePackId) ? PackStatus.Committed : PackStatus.Orphaned;
                pack.CommittedAt = timeProvider.GetUtcNow();
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static async Task<RegisteredChunk> RegisterChunkAsync(CirrusDbContext db, long tenantId,
        PackChunkCandidate candidate, CancellationToken cancellationToken)
    {
        DbConnection connection = db.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            INSERT INTO cirrus.content_chunk (tenant_id, hash_algorithm, chunk_hash, raw_size_bytes, created_at)
            VALUES (@tenant, 'SHA-256', @hash, @size, CURRENT_TIMESTAMP)
            ON CONFLICT (tenant_id, hash_algorithm, chunk_hash) DO NOTHING RETURNING content_chunk_id
            """;
        AddParameter(command, "@tenant", tenantId);
        AddParameter(command, "@hash", candidate.Hash);
        AddParameter(command, "@size", candidate.Length);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not null) return new(Convert.ToInt64(value), true);
        long id = await db.ContentChunks.Where(x => x.TenantId == tenantId && x.HashAlgorithm == "SHA-256"
            && x.ChunkHash == candidate.Hash).Select(x => x.ContentChunkId).SingleAsync(cancellationToken);
        return new(id, false);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
