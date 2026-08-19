using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Exceptions;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;
using MTSM.Cirrus.Worker;

namespace MTSM.Cirrus.Core.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PurgeProcessorIntegrationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task ProcessBatchAsync_DoesNotPurgeBeforeOrOnRetentionDate()
    {
        long id = await AddArchiveObjectAsync(
            ArchiveStatus.DeletionRequested,
            DateOnly.FromDateTime(Now.UtcDateTime));
        await using PurgeWorkerTestContext context = CreateContext();
        await StoreAsync(context, id);

        Assert.Equal(0, await context.Processor.ProcessBatchAsync("worker-a", default));
        Assert.True(await StorageExistsAsync(context, id));

        context.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-a", default));
        Assert.False(await StorageExistsAsync(context, id));
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_DeletesStorageThenAtomicallyMarksPurged()
    {
        long id = await AddArchiveObjectAsync(
            ArchiveStatus.DeletionRequested,
            DateOnly.FromDateTime(Now.AddDays(-1).UtcDateTime));
        await using PurgeWorkerTestContext context = CreateContext();
        await StoreAsync(context, id);

        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-a", default));

        ArchiveObject item = await GetAsync(id);
        Assert.Equal(ArchiveStatus.Purged, item.ArchiveStatus);
        Assert.Equal(Now, item.PurgedAt);
        Assert.Null(item.PurgeLeaseOwner);
        Assert.Contains(item.Events, e => e.EventType == ArchiveEventType.Purged);
        Assert.False(await StorageExistsAsync(context, id));
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_MissingStorageObjectIsAuditedIdempotentSuccess()
    {
        long id = await AddArchiveObjectAsync(
            ArchiveStatus.DeletionRequested,
            DateOnly.FromDateTime(Now.AddDays(-1).UtcDateTime));
        await using PurgeWorkerTestContext context = CreateContext();

        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-a", default));
        Assert.Equal(0, await context.Processor.ProcessBatchAsync("worker-b", default));

        ArchiveObject item = await GetAsync(id);
        ArchiveEvent purged = Assert.Single(item.Events, e => e.EventType == ArchiveEventType.Purged);
        Assert.Contains("objectWasAlreadyMissing", purged.DetailsJson!.RootElement.ToString());
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_StorageFailureSchedulesExponentialRetry()
    {
        long id = await AddArchiveObjectAsync(
            ArchiveStatus.DeletionRequested,
            DateOnly.FromDateTime(Now.AddDays(-1).UtcDateTime));
        var storage = new InMemoryObjectStorage
        {
            DeleteException = new ObjectStorageException("storage unavailable")
        };
        await using PurgeWorkerTestContext context = CreateContext(storage: storage);

        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-a", default));
        Assert.Equal(0, await context.Processor.ProcessBatchAsync("worker-a", default));

        ArchiveObject failed = await GetAsync(id);
        ArchiveErrorQueueItem error = Assert.Single(failed.Errors, e => !e.Resolved);
        Assert.Equal(1, error.RetryCount);
        Assert.Equal(Now.AddMinutes(5), error.NextRetryAt);
        Assert.Contains(failed.Events, e => e.EventType == ArchiveEventType.PurgeFailed);

        context.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-a", default));
        error = Assert.Single((await GetAsync(id)).Errors, e => !e.Resolved);
        Assert.Equal(2, error.RetryCount);
        Assert.Equal(Now.AddMinutes(15), error.NextRetryAt);
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_ExpiredLeaseResumesAfterStorageWasAlreadyDeleted()
    {
        long id = await AddArchiveObjectAsync(
            ArchiveStatus.DeletionRequested,
            DateOnly.FromDateTime(Now.AddDays(-1).UtcDateTime),
            purgeLeaseOwner: "crashed-worker",
            purgeLeaseUntil: Now.AddMinutes(1));
        await using PurgeWorkerTestContext context = CreateContext();

        Assert.Equal(0, await context.Processor.ProcessBatchAsync("worker-a", default));
        context.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-b", default));
        Assert.Equal(ArchiveStatus.Purged, (await GetAsync(id)).ArchiveStatus);
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_CancellationLeavesRecoverableLease()
    {
        long id = await AddArchiveObjectAsync(
            ArchiveStatus.DeletionRequested,
            DateOnly.FromDateTime(Now.AddDays(-1).UtcDateTime));
        var storage = new InMemoryObjectStorage
        {
            BeforeDeleteCompletesAsync = async cancellationToken =>
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
        };
        await using PurgeWorkerTestContext context = CreateContext(storage: storage);
        await StoreAsync(context, id);
        using var cancellation = new CancellationTokenSource();
        Task<int> processing = context.Processor.ProcessBatchAsync(
            "worker-a",
            cancellation.Token);
        await WaitUntilAsync(() => storage.DeleteCallCount == 1);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        ArchiveObject interrupted = await GetAsync(id);
        Assert.Equal(ArchiveStatus.DeletionRequested, interrupted.ArchiveStatus);
        Assert.NotNull(interrupted.PurgeLeaseUntil);

        storage.BeforeDeleteCompletesAsync = null;
        context.Clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-b", default));
        Assert.Equal(ArchiveStatus.Purged, (await GetAsync(id)).ArchiveStatus);
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_TwoWorkersClaimObjectOnlyOnce()
    {
        long id = await AddArchiveObjectAsync(
            ArchiveStatus.DeletionRequested,
            DateOnly.FromDateTime(Now.AddDays(-1).UtcDateTime));
        var storage = new InMemoryObjectStorage();
        await using PurgeWorkerTestContext first = CreateContext(storage: storage);
        await using PurgeWorkerTestContext second = CreateContext(storage: storage);
        await StoreAsync(first, id);

        int[] claimed = await Task.WhenAll(
            first.Processor.ProcessBatchAsync("worker-a", default),
            second.Processor.ProcessBatchAsync("worker-b", default));

        Assert.Equal(1, claimed.Sum());
        Assert.Equal(1, storage.DeleteCallCount);
        Assert.Equal(ArchiveStatus.Purged, (await GetAsync(id)).ArchiveStatus);
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_AutomaticallyRequestsPolicyDeletionAfterExpiry()
    {
        int policyId;
        await using (CirrusDbContext db = CreateDbContext())
        {
            RetentionPolicy policy = await db.RetentionPolicies.SingleAsync();
            policy.DeleteAfterExpiry = true;
            await db.SaveChangesAsync();
            policyId = policy.RetentionPolicyId;
        }
        long id = await AddArchiveObjectAsync(
            ArchiveStatus.Active,
            DateOnly.FromDateTime(Now.AddDays(-1).UtcDateTime),
            policyId: policyId);
        await using PurgeWorkerTestContext context = CreateContext();
        await StoreAsync(context, id);

        Assert.Equal(1, await context.Processor.ProcessBatchAsync("worker-a", default));
        ArchiveObject item = await GetAsync(id);
        Assert.Equal(ArchiveStatus.Purged, item.ArchiveStatus);
        Assert.Contains(item.Events, e => e.EventType == ArchiveEventType.DeletionRequested);
        Assert.Contains(item.Events, e => e.EventType == ArchiveEventType.Purged);
    }

    public Task InitializeAsync() => fixture.ResetAndSeedAsync();
    public Task DisposeAsync() => fixture.ResetAndSeedAsync();

    private PurgeWorkerTestContext CreateContext(InMemoryObjectStorage? storage = null) =>
        PurgeWorkerTestContext.Create(fixture.GetRequiredConnectionString(), Now, storage: storage);
    private CirrusDbContext CreateDbContext() =>
        CoreTestFactory.CreateDbContext(fixture.GetRequiredConnectionString());

    private async Task<long> AddArchiveObjectAsync(
        ArchiveStatus status,
        DateOnly retentionUntil,
        int? policyId = null,
        string? purgeLeaseOwner = null,
        DateTimeOffset? purgeLeaseUntil = null)
    {
        var item = new ArchiveObject
        {
            TenantId = 1,
            ObjectKey = $"purge-tests/{Guid.NewGuid():N}",
            BucketName = "cirrus-test",
            FileType = "test",
            SourceSystem = "purge-suite",
            OriginalFilename = "payload.bin",
            Sha256Hash = new string('a', 64),
            SizeBytes = 7,
            ReceivedAt = Now.AddYears(-1),
            ArchivedAt = Now.AddYears(-1),
            RetentionUntil = retentionUntil,
            RetentionPolicyId = policyId,
            ArchiveStatus = status,
            DeletionRequestedAt = status == ArchiveStatus.DeletionRequested ? Now.AddDays(-2) : null,
            DeletionRequestedBy = status == ArchiveStatus.DeletionRequested ? "requester" : null,
            PurgeLeaseOwner = purgeLeaseOwner,
            PurgeLeaseUntil = purgeLeaseUntil,
            CreatedBy = "purge-suite"
        };
        await using CirrusDbContext db = CreateDbContext();
        db.ArchiveObjects.Add(item);
        await db.SaveChangesAsync();
        return item.ArchiveObjectId;
    }

    private async Task StoreAsync(PurgeWorkerTestContext context, long id)
    {
        ArchiveObject item = await GetAsync(id);
        await using var stream = new MemoryStream("payload"u8.ToArray());
        await context.Storage.WriteAsync(item.BucketName, item.ObjectKey, stream, null);
    }

    private async Task<bool> StorageExistsAsync(PurgeWorkerTestContext context, long id)
    {
        ArchiveObject item = await GetAsync(id);
        return await context.Storage.ExistsAsync(item.BucketName, item.ObjectKey);
    }

    private async Task<ArchiveObject> GetAsync(long id)
    {
        await using CirrusDbContext db = CreateDbContext();
        return await db.ArchiveObjects
            .Include(item => item.Events)
            .Include(item => item.Errors)
            .SingleAsync(item => item.ArchiveObjectId == id);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
