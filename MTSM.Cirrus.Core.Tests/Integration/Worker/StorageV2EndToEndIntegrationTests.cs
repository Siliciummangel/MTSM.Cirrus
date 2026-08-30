using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Services;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;
using MTSM.Cirrus.Worker;
using MTSM.Cirrus.Worker.StorageV2;
using System.Security.Cryptography;

namespace MTSM.Cirrus.Core.Tests;

[Collection(PostgresCollection.Name)]
public sealed class StorageV2EndToEndIntegrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    [PostgresAndS3Fact]
    public async Task PostgreSqlAndS3_StagePackCompressRangeRestoreAndCleanup()
    {
        var s3 = new S3Fixture();
        await s3.InitializeAsync();
        try
        {
            var now = new DateTimeOffset(2026, 8, 30, 16, 0, 0, TimeSpan.Zero);
            byte[] source = Enumerable.Range(0, 512 * 1024).Select(i => (byte)(i % 32)).ToArray();
            await using (CirrusDbContext db = CreateDbContext())
            {
                Tenant tenant = await db.Tenants.SingleAsync(x => x.TenantId == 1);
                tenant.BucketName = s3.BucketName;
                var archive = new ArchiveObject { TenantId = 1,
                    StagingObjectKey = $"objects/tenant-a/staging/{Guid.NewGuid():N}",
                    StorageProcessingStatus = StorageProcessingStatus.Ready,
                    StorageProcessingVerifiedAt = now, BucketName = s3.BucketName, FileType = "binary",
                    SourceSystem = "storage-v2-e2e", OriginalFilename = "payload.bin",
                    Sha256Hash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(),
                    SizeBytes = source.Length, ReceivedAt = now, ArchivedAt = now,
                    RetentionUntil = new DateOnly(2036, 8, 30), ArchiveStatus = ArchiveStatus.Active,
                    CreatedBy = "storage-v2-e2e" };
                db.ArchiveObjects.Add(archive);
                await db.SaveChangesAsync();
                await using var upload = new MemoryStream(source);
                await s3.CreateStorage().WriteAsync(s3.BucketName, archive.StagingObjectKey, upload, null);
            }

            await using var services = CreateProvider(s3.CreateStorage());
            IServiceScopeFactory scopes = services.GetRequiredService<IServiceScopeFactory>();
            var configuredOptions = Options.Create(new StorageProcessingOptions { BatchSize = 10, MaxConcurrency = 1,
                    LeaseDurationMinutes = 3, LeaseHeartbeatSeconds = 30, InitialRetryDelaySeconds = 1,
                    MaximumRetryDelayMinutes = 1, MaximumAttempts = 3, MinimumChunkSizeBytes = 4096,
                    AverageChunkSizeBytes = 8192, MaximumChunkSizeBytes = 16384,
                    TargetPackSizeBytes = 128 * 1024, MaximumBatchWaitSeconds = 0, ZstdCompressionLevel = 3 });
            var clock = new TestTimeProvider(now);
            var processor = new StoragePackingProcessor(scopes,
                new StoragePackingLeaseManager(scopes, configuredOptions, clock,
                    NullLogger<StoragePackingLeaseManager>.Instance),
                new ArchivePackPlanner(scopes, configuredOptions, new FastCdcContentChunker(),
                    new PackWriter(clock), new ManifestCommitter(clock)),
                new StagingFinalizer(scopes, clock), NullLogger<StoragePackingProcessor>.Instance);
            Assert.Equal(1, await processor.ProcessBatchAsync("storage-v2-e2e", default));

            await using CirrusDbContext check = CreateDbContext();
            ArchiveObject completed = await check.ArchiveObjects.SingleAsync(x => x.SourceSystem == "storage-v2-e2e");
            Assert.Equal(StorageProcessingStatus.Completed, completed.StorageProcessingStatus);
            Assert.Null(completed.StagingObjectKey);
            Assert.All(await check.StorageLocations.ToArrayAsync(), x => Assert.Equal("Zstd", x.CompressionAlgorithm));
            var reader = new ManifestContentReader(check, s3.CreateStorage());
            await using Stream restored = await reader.OpenReadAsync(completed.ContentManifestId!.Value);
            using var output = new MemoryStream();
            await restored.CopyToAsync(output);
            Assert.Equal(source, output.ToArray());
        }
        finally
        {
            await s3.DisposeAsync();
        }
    }

    private ServiceProvider CreateProvider(IObjectStorage storage)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDbContext());
        services.AddSingleton(storage);
        services.AddSingleton<IObjectStorage>(storage);
        services.AddScoped<IManifestContentReader, ManifestContentReader>();
        return services.BuildServiceProvider();
    }
    private CirrusDbContext CreateDbContext() => CoreTestFactory.CreateDbContext(postgres.GetRequiredConnectionString());
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => postgres.ResetAndSeedAsync();
}
