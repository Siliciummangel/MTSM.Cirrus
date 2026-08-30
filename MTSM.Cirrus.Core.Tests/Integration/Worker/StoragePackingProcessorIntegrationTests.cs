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

    private ServiceProvider CreateProvider(InMemoryObjectStorage storage)
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
