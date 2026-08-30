using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;
using System.Security.Cryptography;

namespace MTSM.Cirrus.Core.Tests;

[Collection(PostgresCollection.Name)]
public sealed class StorageProcessingProcessorIntegrationTests(
    PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task ProcessBatchAsync_VerifiesStagingAndMarksReady()
    {
        byte[] content = "staged payload"u8.ToArray();
        long id = await AddStagedArchiveAsync(content);
        await using StorageWorkerTestContext context = CreateContext();
        await StoreAsync(context, id, content);

        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-a", default));

        ArchiveObject item = await GetAsync(id);
        Assert.Equal(StorageProcessingStatus.Ready, item.StorageProcessingStatus);
        Assert.Equal(1, item.StorageProcessingAttemptCount);
        Assert.Equal(Now, item.StorageProcessingVerifiedAt);
        Assert.Null(item.StorageProcessingLeaseOwner);
        Assert.NotNull(item.StagingObjectKey);
        Assert.Contains(item.Events, e =>
            e.EventType == ArchiveEventType.StorageProcessingVerified);
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_HashMismatchFailsWithoutDeletingStaging()
    {
        byte[] expected = "expected"u8.ToArray();
        long id = await AddStagedArchiveAsync(expected);
        await using StorageWorkerTestContext context = CreateContext();
        await StoreAsync(context, id, "tampered"u8.ToArray());

        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-a", default));

        ArchiveObject item = await GetAsync(id);
        Assert.Equal(StorageProcessingStatus.Failed, item.StorageProcessingStatus);
        Assert.Equal("STAGING_INTEGRITY_MISMATCH", item.StorageProcessingErrorCode);
        Assert.NotNull(item.StagingObjectKey);
        Assert.Contains(item.Errors, error => !error.Resolved);
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_TransientFailureSchedulesRetry()
    {
        byte[] content = "retry payload"u8.ToArray();
        long id = await AddStagedArchiveAsync(content);
        await using StorageWorkerTestContext context = CreateContext();

        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-a", default));

        ArchiveObject item = await GetAsync(id);
        Assert.Equal(StorageProcessingStatus.RetryPending, item.StorageProcessingStatus);
        Assert.Equal(Now.AddSeconds(30), item.StorageProcessingNextAttemptAt);
        Assert.Null(item.StorageProcessingLeaseOwner);
        Assert.NotNull(item.StagingObjectKey);
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_TwoWorkersClaimObjectOnlyOnce()
    {
        byte[] content = "concurrent payload"u8.ToArray();
        long id = await AddStagedArchiveAsync(content);
        await using StorageWorkerTestContext first = CreateContext();
        await using StorageWorkerTestContext second = CreateContext(storage: first.Storage);
        await StoreAsync(first, id, content);

        int[] claimed = await Task.WhenAll(
            first.Processor.ProcessBatchAsync("worker-a", default),
            second.Processor.ProcessBatchAsync("worker-b", default));

        Assert.Equal(1, claimed.Sum());
        Assert.Equal(StorageProcessingStatus.Ready, (await GetAsync(id)).StorageProcessingStatus);
    }

    private StorageWorkerTestContext CreateContext(
        InMemoryObjectStorage? storage = null) =>
        StorageWorkerTestContext.Create(
            fixture.GetRequiredConnectionString(),
            Now,
            storage: storage);

    private async Task<long> AddStagedArchiveAsync(byte[] content)
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        Tenant tenant = await dbContext.Tenants.SingleAsync(item => item.TenantId == 1);
        var item = new ArchiveObject
        {
            TenantId = tenant.TenantId,
            ObjectKey = null,
            StagingObjectKey = $"staging-tests/{Guid.NewGuid():N}",
            StorageProcessingStatus = StorageProcessingStatus.Staged,
            BucketName = tenant.BucketName,
            FileType = "document",
            SourceSystem = "storage-worker-tests",
            OriginalFilename = "payload.bin",
            Sha256Hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            SizeBytes = content.Length,
            ReceivedAt = Now,
            ArchivedAt = Now,
            RetentionUntil = new DateOnly(2036, 8, 27),
            ArchiveStatus = ArchiveStatus.Active,
            CreatedBy = "test-suite"
        };
        dbContext.ArchiveObjects.Add(item);
        await dbContext.SaveChangesAsync();
        return item.ArchiveObjectId;
    }

    private async Task StoreAsync(
        StorageWorkerTestContext context,
        long id,
        byte[] content)
    {
        ArchiveObject item = await GetAsync(id);
        await using var stream = new MemoryStream(content);
        await context.Storage.WriteAsync(
            item.BucketName,
            item.StagingObjectKey!,
            stream,
            "application/octet-stream");
    }

    private async Task<ArchiveObject> GetAsync(long id)
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        return await dbContext.ArchiveObjects
            .AsNoTracking()
            .Include(item => item.Events)
            .Include(item => item.Errors)
            .SingleAsync(item => item.ArchiveObjectId == id);
    }

    private CirrusDbContext CreateDbContext() =>
        CoreTestFactory.CreateDbContext(fixture.GetRequiredConnectionString());

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fixture.ResetAndSeedAsync();
}
