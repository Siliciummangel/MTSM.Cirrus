using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;
using MTSM.Cirrus.Worker;
using MTSM.Cirrus.Worker.Maintenance;
using System.Security.Cryptography;

namespace MTSM.Cirrus.Core.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PackMaintenanceProcessorIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 15, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task ProcessBatchAsync_AtomicallyRepacksLiveLocationsThenCollectsOldPack()
    {
        var storage = new InMemoryObjectStorage();
        byte[] packBytes = Enumerable.Range(0, 100).Select(x => (byte)x).ToArray();
        const string oldKey = "objects/tenant-a/packs/v1/old";
        await using (var content = new MemoryStream(packBytes))
            await storage.WriteAsync("cirrus-test", oldKey, content, null);

        long oldPackId;
        await using (CirrusDbContext db = CreateDbContext())
        {
            var chunk = new ContentChunk { TenantId = 1, HashAlgorithm = "SHA-256",
                ChunkHash = Convert.ToHexString(SHA256.HashData(packBytes.AsSpan(10, 10))).ToLowerInvariant(),
                RawSizeBytes = 10, CreatedAt = Now.AddHours(-2) };
            var pack = new StoragePack { TenantId = 1, BucketName = "cirrus-test", ObjectKey = oldKey,
                HashAlgorithm = "SHA-256", PackHash = new('b', 64), SizeBytes = 100,
                PackStatus = PackStatus.Committed, CreatedAt = Now.AddHours(-2), UploadedAt = Now.AddHours(-2),
                CommittedAt = Now.AddHours(-2) };
            db.AddRange(chunk, pack);
            await db.SaveChangesAsync();
            oldPackId = pack.StoragePackId;
            db.StorageLocations.Add(new StorageLocation { ContentChunkId = chunk.ContentChunkId,
                StoragePackId = pack.StoragePackId, PackOffset = 10, StoredLength = 10, RawLength = 10,
                CompressionAlgorithm = "None", CompressionVersion = 0, StorageFormatVersion = 1, CreatedAt = Now });
            var manifest = new ContentManifest { TenantId = 1, ManifestFormatVersion = 1,
                HashAlgorithm = "SHA-256", OriginalHash = new('c', 64), OriginalSizeBytes = 10,
                ChunkingAlgorithm = "FastCDC", ChunkingAlgorithmVersion = 1, MinimumChunkSizeBytes = 1,
                AverageChunkSizeBytes = 2, MaximumChunkSizeBytes = 4, ChunkCount = 1, CommittedAt = Now };
            manifest.Chunks.Add(new ManifestChunk { SequenceNumber = 0, OriginalOffset = 0,
                RawLength = 10, ContentChunkId = chunk.ContentChunkId });
            var archive = new ArchiveObject { TenantId = 1, ContentManifest = manifest,
                StorageProcessingStatus = StorageProcessingStatus.Completed, BucketName = "cirrus-test",
                FileType = "binary", SourceSystem = "maintenance-test", OriginalFilename = "payload.bin",
                Sha256Hash = new('c', 64), SizeBytes = 10, ReceivedAt = Now, ArchivedAt = Now,
                RetentionUntil = new DateOnly(2036, 8, 30), ArchiveStatus = ArchiveStatus.Active,
                CreatedBy = "maintenance-test" };
            db.Add(archive);
            await db.SaveChangesAsync();
        }

        await using ServiceProvider provider = CreateProvider(storage);
        PackMaintenanceProcessor processor = CreateProcessor(provider);
        Assert.Equal(1, await processor.ProcessBatchAsync("maintenance-test", default));

        await using (CirrusDbContext db = CreateDbContext())
        {
            StorageLocation location = await db.StorageLocations.Include(x => x.StoragePack).SingleAsync();
            Assert.NotEqual(oldPackId, location.StoragePackId);
            Assert.Equal(PackStatus.Committed, location.StoragePack.PackStatus);
            Assert.Equal(PackStatus.GarbagePending,
                (await db.StoragePacks.SingleAsync(x => x.StoragePackId == oldPackId)).PackStatus);
        }

        Assert.Equal(1, await processor.ProcessBatchAsync("maintenance-test", default));
        await using (CirrusDbContext db = CreateDbContext())
            Assert.DoesNotContain(await db.StoragePacks.ToArrayAsync(), x => x.StoragePackId == oldPackId);
        Assert.False(await storage.ExistsAsync("cirrus-test", oldKey));
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_CollectsUnreferencedOrphanAfterGracePeriod()
    {
        var storage = new InMemoryObjectStorage();
        const string key = "objects/tenant-a/packs/v1/orphan";
        await using (var content = new MemoryStream([1, 2, 3]))
            await storage.WriteAsync("cirrus-test", key, content, null);
        await using (CirrusDbContext db = CreateDbContext())
        {
            db.StoragePacks.Add(new StoragePack { TenantId = 1, BucketName = "cirrus-test", ObjectKey = key,
                HashAlgorithm = "SHA-256", SizeBytes = 3, PackStatus = PackStatus.Orphaned,
                CreatedAt = Now.AddHours(-2), UploadedAt = Now.AddHours(-2) });
            await db.SaveChangesAsync();
        }
        await using ServiceProvider provider = CreateProvider(storage);
        PackMaintenanceProcessor processor = CreateProcessor(provider);
        Assert.Equal(1, await processor.ProcessBatchAsync("maintenance-test", default));
        await using CirrusDbContext check = CreateDbContext();
        Assert.Empty(await check.StoragePacks.ToArrayAsync());
        Assert.False(await storage.ExistsAsync("cirrus-test", key));
    }

    private static StorageProcessingOptions OptionsForTest() => new() { LeaseDurationMinutes = 3,
        PackMaintenanceBatchSize = 10, OrphanGracePeriodMinutes = 60,
        CompactionMinimumAgeMinutes = 60, CompactionUtilizationPercent = 70, TargetPackSizeBytes = 1024 };
    private static PackMaintenanceProcessor CreateProcessor(ServiceProvider provider)
    {
        IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(OptionsForTest());
        var clock = new TestTimeProvider(Now);
        return new PackMaintenanceProcessor(scopes, new UnreachableContentCollector(scopes),
            new PackGarbageCollector(scopes, options, clock, NullLogger<PackGarbageCollector>.Instance),
            new PackMaintenanceLeaseManager(scopes, options, clock,
                NullLogger<PackMaintenanceLeaseManager>.Instance),
            new PackCompactor(scopes, options, clock), NullLogger<PackMaintenanceProcessor>.Instance);
    }
    private ServiceProvider CreateProvider(InMemoryObjectStorage storage)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDbContext());
        services.AddSingleton<IObjectStorage>(storage);
        return services.BuildServiceProvider();
    }
    private CirrusDbContext CreateDbContext() => CoreTestFactory.CreateDbContext(fixture.GetRequiredConnectionString());
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fixture.ResetAndSeedAsync();
}
