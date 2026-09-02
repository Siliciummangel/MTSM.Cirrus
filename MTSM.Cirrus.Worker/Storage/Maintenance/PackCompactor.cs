using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Worker.StorageV2;
using System.Security.Cryptography;
using ZstdSharp;

namespace MTSM.Cirrus.Worker.Maintenance;

public sealed class PackCompactor(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageProcessingOptions> options,
    TimeProvider timeProvider)
{
    private readonly StorageProcessingOptions _options = options.Value;

    public async Task CompactAsync(StoragePack[] oldPacks, string lease, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        CirrusDbContext db = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
        IObjectStorage storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        long[] oldIds = oldPacks.Select(x => x.StoragePackId).ToArray();
        StorageLocation[] locations = await db.StorageLocations.AsNoTracking()
            .Include(x => x.StoragePack).Include(x => x.ContentChunk)
            .Where(x => oldIds.Contains(x.StoragePackId))
            .OrderBy(x => x.StoragePackId).ThenBy(x => x.PackOffset).ToArrayAsync(cancellationToken);
        var moved = new List<MovedPackLocation>();
        var pending = new List<(StorageLocation Source, PackEntry Entry)>();
        var newPackIds = new List<long>();
        StreamingPackWriter? builder = null;
        string? key = null;

        StreamingPackWriter StartPack()
        {
            string prefix = oldPacks[0].ObjectKey.Split("/packs/", StringSplitOptions.None)[0];
            key = $"{prefix}/packs/v1/{timeProvider.GetUtcNow():yyyy/MM/dd}/{Guid.NewGuid():N}";
            return new StreamingPackWriter(storage, oldPacks[0].BucketName, key, cancellationToken);
        }

        async Task FlushAsync()
        {
            if (builder is null || builder.Length == 0) return;
            await using StreamingPackWriter current = builder;
            builder = null;
            UploadedPackContent uploaded = await current.CompleteAsync(cancellationToken);
            DateTimeOffset now = timeProvider.GetUtcNow();
            ObjectStorageWriteResult write = uploaded.Write;
            var pack = new StoragePack
            {
                TenantId = oldPacks[0].TenantId,
                BucketName = oldPacks[0].BucketName,
                ObjectKey = key!,
                StorageVersionId = write.VersionId ?? write.ETag,
                PackFormatVersion = 1,
                HashAlgorithm = "SHA-256",
                PackHash = uploaded.Sha256Hash,
                SizeBytes = uploaded.Length,
                PackStatus = PackStatus.Uploaded,
                CreatedAt = now,
                UploadedAt = now
            };
            db.StoragePacks.Add(pack);
            await db.SaveChangesAsync(cancellationToken);
            newPackIds.Add(pack.StoragePackId);
            moved.AddRange(pending.Select(x => new MovedPackLocation(x.Source, pack.StoragePackId, x.Entry)));
            pending.Clear();
        }

        try
        {
            foreach (StorageLocation location in locations)
            {
                builder ??= StartPack();
                if (builder.Length > 0 && builder.Length + location.StoredLength > _options.TargetPackSizeBytes)
                { await FlushAsync(); builder = StartPack(); }
                await using Stream range = await storage.OpenReadRangeAsync(location.StoragePack.BucketName,
                    location.StoragePack.ObjectKey, location.PackOffset, location.StoredLength, cancellationToken);
                byte[] stored = new byte[location.StoredLength];
                await range.ReadExactlyAsync(stored, cancellationToken);
                Verify(location, stored);
                PackEntry entry = await builder.AppendAsync(stored, location.RawLength, cancellationToken);
                pending.Add((location, entry));
            }
            await FlushAsync();
            await SwapLocationsAsync(db, oldIds, newPackIds, moved, lease, cancellationToken);
        }
        finally
        {
            if (builder is not null) await builder.DisposeAsync();
        }
    }

    private async Task SwapLocationsAsync(CirrusDbContext db, long[] oldIds, List<long> newPackIds,
        List<MovedPackLocation> moved, string lease, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            int owned = await db.StoragePacks.CountAsync(x => oldIds.Contains(x.StoragePackId)
                && x.PackStatus == PackStatus.Committed && x.MaintenanceLeaseOwner == lease, cancellationToken);
            if (owned != oldIds.Length) throw new InvalidOperationException("Pack-maintenance lease was lost.");
            foreach (MovedPackLocation move in moved)
                db.StorageLocations.Add(new StorageLocation
                {
                    ContentChunkId = move.Source.ContentChunkId,
                    StoragePackId = move.NewPackId,
                    PackOffset = move.Entry.Offset,
                    StoredLength = move.Entry.StoredLength,
                    RawLength = move.Source.RawLength,
                    CompressionAlgorithm = move.Source.CompressionAlgorithm,
                    CompressionVersion = move.Source.CompressionVersion,
                    StorageFormatVersion = move.Source.StorageFormatVersion,
                    CreatedAt = timeProvider.GetUtcNow()
                });
            await db.StorageLocations.Where(x => oldIds.Contains(x.StoragePackId)).ExecuteDeleteAsync(cancellationToken);
            await db.StoragePacks.Where(x => newPackIds.Contains(x.StoragePackId)).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PackStatus, PackStatus.Committed)
                .SetProperty(x => x.CommittedAt, timeProvider.GetUtcNow()), cancellationToken);
            await db.StoragePacks.Where(x => oldIds.Contains(x.StoragePackId)).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.PackStatus, PackStatus.GarbagePending)
                .SetProperty(x => x.MaintenanceLeaseOwner, (string?)null)
                .SetProperty(x => x.MaintenanceLeaseUntil, (DateTimeOffset?)null), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static void Verify(StorageLocation location, byte[] stored)
    {
        byte[] raw = location.CompressionAlgorithm switch
        {
            "None" => stored,
            "Zstd" => Decompress(stored, location.RawLength),
            _ => throw new InvalidOperationException($"Unsupported compression algorithm '{location.CompressionAlgorithm}'.")
        };
        string hash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        if (raw.Length != location.RawLength
            || !string.Equals(hash, location.ContentChunk.ChunkHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Chunk {location.ContentChunkId} failed verification during compaction.");
    }

    private static byte[] Decompress(byte[] stored, int rawLength)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(stored, rawLength).ToArray();
    }
}
