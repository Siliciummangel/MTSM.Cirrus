using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Core.Services;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;
using MTSM.Cirrus.Worker;
using MTSM.Cirrus.Worker.StorageV2;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

namespace MTSM.Cirrus.Core.Tests;

[Collection(PostgresCollection.Name)]
public sealed class StoragePackingProcessorIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task ProcessBatchAsync_PacksRestoresDeduplicatesAndCleansUp()
    {
        byte[] repeated = Enumerable.Range(0, 64 * 1024).Select(i => (byte)(i * 17)).ToArray();
        long firstId = await AddReadyAsync(repeated);
        long secondId = await AddReadyAsync(repeated);
        var storage = new InMemoryObjectStorage();
        await StoreStagingAsync(storage, firstId, repeated);
        await StoreStagingAsync(storage, secondId, repeated);
        await using ServiceProvider provider = CreateProvider(storage);
        IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var configuredOptions = Options.Create(new StorageProcessingOptions
            {
                BatchSize = 10, MaxConcurrency = 1, LeaseDurationMinutes = 3,
                InitialRetryDelaySeconds = 1, MaximumRetryDelayMinutes = 1, MaximumAttempts = 3,
                MinimumChunkSizeBytes = 1024, AverageChunkSizeBytes = 2048,
                MaximumChunkSizeBytes = 8192, TargetPackSizeBytes = 32 * 1024,
                MaximumBatchWaitSeconds = 0
            });
        var clock = new TestTimeProvider(Now);
        var committer = new ManifestCommitter(clock);
        var processor = new StoragePackingProcessor(scopes,
            new StoragePackingLeaseManager(scopes, configuredOptions, clock,
                NullLogger<StoragePackingLeaseManager>.Instance),
            new ArchivePackPlanner(scopes, configuredOptions, new FastCdcContentChunker(),
                new PackWriter(clock), committer),
            new StagingFinalizer(scopes, clock), NullLogger<StoragePackingProcessor>.Instance);

        Assert.Equal(2, await processor.ProcessBatchAsync("packing-test", default));

        await using CirrusDbContext db = CreateDbContext();
        ArchiveObject[] archives = await db.ArchiveObjects
            .Where(x => x.ArchiveObjectId == firstId || x.ArchiveObjectId == secondId)
            .OrderBy(x => x.ArchiveObjectId).ToArrayAsync();
        Assert.All(archives, archive =>
        {
            Assert.Equal(StorageProcessingStatus.Completed, archive.StorageProcessingStatus);
            Assert.NotNull(archive.ContentManifestId);
            Assert.Null(archive.StagingObjectKey);
        });
        Assert.Equal(2, await db.ContentManifests.CountAsync(x =>
            x.ContentManifestId == archives[0].ContentManifestId
            || x.ContentManifestId == archives[1].ContentManifestId));
        int manifestReferences = await db.ManifestChunks.CountAsync(x =>
            x.ContentManifestId == archives[0].ContentManifestId
            || x.ContentManifestId == archives[1].ContentManifestId);
        int uniqueChunks = await db.ContentChunks.CountAsync(x => x.TenantId == 1);
        Assert.True(manifestReferences > uniqueChunks);
        Assert.All(await db.StoragePacks.ToArrayAsync(), pack =>
            Assert.Contains(pack.PackStatus, new[] { PackStatus.Committed, PackStatus.Orphaned }));
        Assert.All(await db.StorageLocations.ToArrayAsync(), location =>
        {
            Assert.Equal("Zstd", location.CompressionAlgorithm);
            Assert.Equal(1, location.CompressionVersion);
            Assert.True(location.StoredLength < location.RawLength);
        });

        var reader = new ManifestContentReader(db, storage);
        await using Stream restored = await reader.OpenReadAsync(archives[0].ContentManifestId!.Value);
        using var output = new MemoryStream();
        await restored.CopyToAsync(output);
        Assert.Equal(repeated, output.ToArray());

        ArchiveService archiveService = CoreTestFactory.CreateService(db, storage);
        ArchiveDownloadResult download = await archiveService.DownloadAsync(1, firstId, "packing-test");
        await using Stream downloaded = download.Content;
        using var downloadedOutput = new MemoryStream();
        await downloaded.CopyToAsync(downloadedOutput);
        Assert.Equal(repeated, downloadedOutput.ToArray());
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_FileLargerThanTargetPacks_RestoresAndVerifiesEveryPack()
    {
        byte[] source = new byte[256 * 1024 + 71];
        new Random(71).NextBytes(source);
        long id = await AddReadyAsync(source);
        var storage = new InMemoryObjectStorage();
        await StoreStagingAsync(storage, id, source);
        await using ServiceProvider provider = CreateProvider(storage);
        StoragePackingProcessor processor = CreateProcessor(provider);

        Assert.Equal(1, await processor.ProcessBatchAsync("multi-pack", default));
        await using CirrusDbContext db = CreateDbContext();
        ArchiveObject archive = await db.ArchiveObjects.SingleAsync(x => x.ArchiveObjectId == id);
        Assert.Equal(StorageProcessingStatus.Completed, archive.StorageProcessingStatus);
        StoragePack[] packs = await db.StoragePacks.ToArrayAsync();
        Assert.True(packs.Length > 1);
        foreach (StoragePack pack in packs)
        {
            Assert.Equal(PackStatus.Committed, pack.PackStatus);
            Assert.InRange(pack.SizeBytes, 1, 32 * 1024);
            await using Stream content = await storage.OpenReadAsync(pack.BucketName, pack.ObjectKey);
            Assert.Equal(pack.SizeBytes, content.Length);
            Assert.Equal(pack.PackHash, Convert.ToHexString(await SHA256.HashDataAsync(content)).ToLowerInvariant());
            StorageLocation[] locations = await db.StorageLocations.Where(x => x.StoragePackId == pack.StoragePackId)
                .OrderBy(x => x.PackOffset).ToArrayAsync();
            long offset = 0;
            foreach (StorageLocation location in locations)
            {
                Assert.Equal(offset, location.PackOffset);
                offset += location.StoredLength;
            }
            Assert.Equal(pack.SizeBytes, offset);
        }
        var reader = new ManifestContentReader(db, storage);
        await using Stream restored = await reader.OpenReadAsync(archive.ContentManifestId!.Value);
        using var output = new MemoryStream();
        await restored.CopyToAsync(output);
        Assert.Equal(source, output.ToArray());
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_LaterPackUploadFails_LeavesOnlyCompleteUploadedPacksAndCanRetry()
    {
        byte[] source = new byte[128 * 1024];
        new Random(42).NextBytes(source);
        long id = await AddReadyAsync(source);
        var storage = new InMemoryObjectStorage();
        await StoreStagingAsync(storage, id, source);
        int writes = 0;
        storage.BeforeWriteCompletesAsync = _ => ++writes == 2
            ? Task.FromException(new IOException("Upload failed.")) : Task.CompletedTask;
        await using ServiceProvider provider = CreateProvider(storage);
        StoragePackingProcessor processor = CreateProcessor(provider);

        await processor.ProcessBatchAsync("failure-test", default);
        await using (CirrusDbContext db = CreateDbContext())
        {
            Assert.Empty(await db.ContentManifests.ToArrayAsync());
            Assert.Empty(await db.ContentChunks.ToArrayAsync());
            Assert.Empty(await db.StorageLocations.ToArrayAsync());
            StoragePack pack = Assert.Single(await db.StoragePacks.ToArrayAsync());
            Assert.Equal(PackStatus.Uploaded, pack.PackStatus);
            Assert.True(await storage.ExistsAsync(pack.BucketName, pack.ObjectKey));
            ArchiveObject archive = await db.ArchiveObjects.SingleAsync(x => x.ArchiveObjectId == id);
            Assert.Null(archive.ContentManifestId);
            Assert.True(await storage.ExistsAsync(archive.BucketName, archive.StagingObjectKey!));
            archive.StorageProcessingNextAttemptAt = null;
            await db.SaveChangesAsync();
        }

        storage.BeforeWriteCompletesAsync = null;
        await processor.ProcessBatchAsync("retry-test", default);
        await using CirrusDbContext check = CreateDbContext();
        ArchiveObject completed = await check.ArchiveObjects.SingleAsync(x => x.ArchiveObjectId == id);
        Assert.Equal(StorageProcessingStatus.Completed, completed.StorageProcessingStatus);
        var reader = new ManifestContentReader(check, storage);
        await using Stream restored = await reader.OpenReadAsync(completed.ContentManifestId!.Value);
        Assert.Equal(SHA256.HashData(source), await SHA256.HashDataAsync(restored));
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_CancelledUpload_DoesNotPublishMetadataOrRemoveStaging()
    {
        byte[] source = new byte[64 * 1024];
        new Random(19).NextBytes(source);
        long id = await AddReadyAsync(source);
        var storage = new InMemoryObjectStorage();
        await StoreStagingAsync(storage, id, source);
        using var cancellation = new CancellationTokenSource();
        storage.BeforeWriteCompletesAsync = token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        await using ServiceProvider provider = CreateProvider(storage);
        StoragePackingProcessor processor = CreateProcessor(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            processor.ProcessBatchAsync("cancel-test", cancellation.Token));
        await using CirrusDbContext db = CreateDbContext();
        Assert.Empty(await db.StoragePacks.ToArrayAsync());
        Assert.Empty(await db.ContentChunks.ToArrayAsync());
        Assert.Empty(await db.ContentManifests.ToArrayAsync());
        Assert.Empty(await db.StorageLocations.ToArrayAsync());
        ArchiveObject archive = await db.ArchiveObjects.SingleAsync(x => x.ArchiveObjectId == id);
        Assert.Null(archive.ContentManifestId);
        Assert.True(await storage.ExistsAsync(archive.BucketName, archive.StagingObjectKey!));
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_ProducerFailsBeforeFlush_AbortsActiveUpload()
    {
        byte[] source = new byte[64 * 1024];
        new Random(9).NextBytes(source);
        long id = await AddReadyAsync(source);
        var staging = new InMemoryObjectStorage();
        await StoreStagingAsync(staging, id, source);
        using var client = new RecordingS3Client();
        using var uploads = client.CreateStorage();
        await using ServiceProvider provider = CreateProvider(new PackUploadObjectStorage(staging, uploads));
        StoragePackingProcessor processor = CreateProcessor(provider, new FailingChunker());

        await processor.ProcessBatchAsync("producer-failure", default).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(1, client.Initiated);
        Assert.Equal(1, client.Aborted);
        Assert.Equal(0, client.Completed);
        Assert.True(staging.LastReadStream!.IsDisposed);
        await using CirrusDbContext db = CreateDbContext();
        Assert.Empty(await db.StoragePacks.ToArrayAsync());
        Assert.Empty(await db.ContentChunks.ToArrayAsync());
        Assert.Empty(await db.ContentManifests.ToArrayAsync());
        Assert.Empty(await db.StorageLocations.ToArrayAsync());
        ArchiveObject archive = await db.ArchiveObjects.SingleAsync(x => x.ArchiveObjectId == id);
        Assert.True(await staging.ExistsAsync(archive.BucketName, archive.StagingObjectKey!));
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_CommitLosesLease_RollsBackReferencesAndRetainsUploadedPack()
    {
        byte[] source = new byte[16 * 1024];
        new Random(9).NextBytes(source);
        long id = await AddReadyAsync(source);
        var storage = new InMemoryObjectStorage();
        await StoreStagingAsync(storage, id, source);
        storage.BeforeWriteCompletesAsync = async _ =>
        {
            await using CirrusDbContext db = CreateDbContext();
            await db.ArchiveObjects.Where(x => x.ArchiveObjectId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StorageProcessingLeaseOwner, "other-worker"));
        };
        await using ServiceProvider provider = CreateProvider(storage);
        await CreateProcessor(provider).ProcessBatchAsync("lost-lease", default);

        await using CirrusDbContext check = CreateDbContext();
        Assert.Empty(await check.ContentChunks.ToArrayAsync());
        Assert.Empty(await check.ContentManifests.ToArrayAsync());
        Assert.Empty(await check.StorageLocations.ToArrayAsync());
        StoragePack pack = Assert.Single(await check.StoragePacks.ToArrayAsync());
        Assert.Equal(PackStatus.Uploaded, pack.PackStatus);
        Assert.True(await storage.ExistsAsync(pack.BucketName, pack.ObjectKey));
    }

    private sealed class FailingChunker : IContentChunker
    {
        public async IAsyncEnumerable<ContentChunkData> ChunkAsync(Stream source, ChunkingProfile profile,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (ContentChunkData chunk in new FastCdcContentChunker().ChunkAsync(source, profile, cancellationToken))
            {
                yield return chunk;
                throw new IOException("Reading the next staged chunk failed.");
            }
        }
    }

    private static StoragePackingProcessor CreateProcessor(ServiceProvider provider, IContentChunker? chunker = null)
    {
        IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new StorageProcessingOptions
        {
            BatchSize = 10, MaxConcurrency = 1, LeaseDurationMinutes = 3,
            InitialRetryDelaySeconds = 1, MaximumRetryDelayMinutes = 1, MaximumAttempts = 3,
            MinimumChunkSizeBytes = 1024, AverageChunkSizeBytes = 2048,
            MaximumChunkSizeBytes = 8192, TargetPackSizeBytes = 32 * 1024, MaximumBatchWaitSeconds = 0
        });
        var clock = new TestTimeProvider(Now);
        return new StoragePackingProcessor(scopes,
            new StoragePackingLeaseManager(scopes, options, clock, NullLogger<StoragePackingLeaseManager>.Instance),
            new ArchivePackPlanner(scopes, options, chunker ?? new FastCdcContentChunker(), new PackWriter(clock), new ManifestCommitter(clock)),
            new StagingFinalizer(scopes, clock), NullLogger<StoragePackingProcessor>.Instance);
    }

    private ServiceProvider CreateProvider(IObjectStorage storage)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDbContext());
        services.AddSingleton<IObjectStorage>(storage);
        services.AddScoped<IManifestContentReader, ManifestContentReader>();
        return services.BuildServiceProvider();
    }

    private async Task<long> AddReadyAsync(byte[] content)
    {
        await using CirrusDbContext db = CreateDbContext();
        Tenant tenant = await db.Tenants.SingleAsync(x => x.TenantId == 1);
        var archive = new ArchiveObject
        {
            TenantId = 1, ObjectKey = null, StagingObjectKey = $"packing-tests/{Guid.NewGuid():N}",
            StorageProcessingStatus = StorageProcessingStatus.Ready,
            StorageProcessingVerifiedAt = Now, BucketName = tenant.BucketName,
            FileType = "binary", SourceSystem = "packing-tests", OriginalFilename = "payload.bin",
            Sha256Hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            SizeBytes = content.Length, ReceivedAt = Now, ArchivedAt = Now,
            RetentionUntil = new DateOnly(2036, 8, 30), ArchiveStatus = ArchiveStatus.Active,
            CreatedBy = "packing-tests"
        };
        db.ArchiveObjects.Add(archive);
        await db.SaveChangesAsync();
        return archive.ArchiveObjectId;
    }

    private async Task StoreStagingAsync(InMemoryObjectStorage storage, long id, byte[] content)
    {
        await using CirrusDbContext db = CreateDbContext();
        ArchiveObject archive = await db.ArchiveObjects.SingleAsync(x => x.ArchiveObjectId == id);
        await using var stream = new MemoryStream(content);
        await storage.WriteAsync(archive.BucketName, archive.StagingObjectKey!, stream, null);
    }

    private CirrusDbContext CreateDbContext() =>
        CoreTestFactory.CreateDbContext(fixture.GetRequiredConnectionString());

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fixture.ResetAndSeedAsync();
}
