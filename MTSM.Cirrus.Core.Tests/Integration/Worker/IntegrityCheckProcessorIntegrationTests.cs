using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;
using MTSM.Cirrus.Worker;

namespace MTSM.Cirrus.Core.Tests;

[Collection(PostgresCollection.Name)]
public sealed class IntegrityCheckProcessorIntegrationTests(
    PostgresFixture fixture) : IAsyncLifetime
{
    [PostgresFact]
    public async Task ProcessBatchAsync_ActiveLeaseIsSkippedAndExpiredLeaseIsReclaimed()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        long archiveObjectId = await AddDueArchiveObjectAsync(
            leaseOwner: "other-worker/active-lease",
            leaseUntil: now.AddMinutes(2));

        await using WorkerTestContext context = CreateContext();

        int claimedWithActiveLease = await context.Processor.ProcessBatchAsync(
            "worker-a",
            CancellationToken.None);

        Assert.Equal(0, claimedWithActiveLease);
        Assert.Empty(context.ArchiveService.CallCounts);

        await using (CirrusDbContext dbContext = CreateDbContext())
        {
            await dbContext.ArchiveObjects
                .Where(item => item.ArchiveObjectId == archiveObjectId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    item => item.IntegrityCheckLeaseUntil,
                    now.AddMinutes(-1)));
        }

        int claimedWithExpiredLease = await context.Processor.ProcessBatchAsync(
            "worker-b",
            CancellationToken.None);

        Assert.Equal(1, claimedWithExpiredLease);
        Assert.Equal(1, context.ArchiveService.CallCounts[archiveObjectId]);

        ArchiveObject persisted = await GetArchiveObjectAsync(archiveObjectId);
        Assert.Null(persisted.IntegrityCheckLeaseOwner);
        Assert.Null(persisted.IntegrityCheckLeaseUntil);
        Assert.NotNull(persisted.LastIntegrityCheckAt);
        Assert.True(persisted.NextIntegrityCheckAt > now);
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_TwoWorkersProcessEachObjectExactlyOnce()
    {
        long firstId = await AddDueArchiveObjectAsync();
        long secondId = await AddDueArchiveObjectAsync();
        IntegrityCheckOptions options = WorkerTestContext.CreateOptions(
            batchSize: 1,
            maxConcurrentChecks: 1);
        await using WorkerTestContext firstWorker =
            CreateContext(options);
        await using WorkerTestContext secondWorker =
            CreateContext(options);

        var bothWorkersEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0;

        async Task<ArchiveIntegrityResult> VerifyAsync(
            long archiveObjectId,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref entered) == 2)
            {
                bothWorkersEntered.TrySetResult();
            }

            await bothWorkersEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);

            return CreateIntegrityResult(archiveObjectId);
        }

        firstWorker.ArchiveService.VerifyAsync = VerifyAsync;
        secondWorker.ArchiveService.VerifyAsync = VerifyAsync;

        Task<int> firstRun = firstWorker.Processor.ProcessBatchAsync(
            "worker-a",
            CancellationToken.None);
        Task<int> secondRun = secondWorker.Processor.ProcessBatchAsync(
            "worker-b",
            CancellationToken.None);

        int[] claimed = await Task.WhenAll(firstRun, secondRun);

        Assert.Equal(2, claimed.Sum());
        Assert.All(claimed, count => Assert.Equal(1, count));
        Assert.Equal(1, GetCallCount(firstWorker, secondWorker, firstId));
        Assert.Equal(1, GetCallCount(firstWorker, secondWorker, secondId));

        await using CirrusDbContext dbContext = CreateDbContext();
        ArchiveObject[] objects = await dbContext.ArchiveObjects
            .Where(item => item.ArchiveObjectId == firstId
                || item.ArchiveObjectId == secondId)
            .ToArrayAsync();

        Assert.All(objects, item =>
        {
            Assert.Null(item.IntegrityCheckLeaseOwner);
            Assert.Null(item.IntegrityCheckLeaseUntil);
            Assert.NotNull(item.LastIntegrityCheckAt);
        });
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_RepeatedFailureUpdatesSingleRetryEntry()
    {
        long archiveObjectId = await AddDueArchiveObjectAsync();
        await using WorkerTestContext context = CreateContext();
        context.ArchiveService.VerifyAsync = (_, _) =>
            throw new InvalidOperationException("storage temporarily unavailable");

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        int firstClaim = await context.Processor.ProcessBatchAsync(
            "worker-a",
            CancellationToken.None);

        Assert.Equal(1, firstClaim);

        await MakeDueAgainAsync(archiveObjectId);

        int secondClaim = await context.Processor.ProcessBatchAsync(
            "worker-a",
            CancellationToken.None);

        Assert.Equal(1, secondClaim);

        await using CirrusDbContext dbContext = CreateDbContext();
        ArchiveObject archiveObject = await dbContext.ArchiveObjects
            .Include(item => item.Errors)
            .Include(item => item.Events)
            .SingleAsync(item => item.ArchiveObjectId == archiveObjectId);
        ArchiveErrorQueueItem error = Assert.Single(archiveObject.Errors);

        Assert.Equal("INTEGRITY_CHECK_FAILED", error.ErrorType);
        Assert.Equal(2, error.RetryCount);
        Assert.False(error.Resolved);
        Assert.NotNull(error.NextRetryAt);
        Assert.True(error.NextRetryAt > startedAt);
        Assert.Equal(error.NextRetryAt, archiveObject.NextIntegrityCheckAt);
        Assert.Null(archiveObject.IntegrityCheckLeaseOwner);
        Assert.Null(archiveObject.IntegrityCheckLeaseUntil);
        Assert.Equal(
            2,
            archiveObject.Events.Count(item =>
                item.EventType == ArchiveEventType.ErrorOccurred));
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_SuccessResolvesPendingRetry()
    {
        long archiveObjectId = await AddDueArchiveObjectAsync(
            addPendingRetry: true);
        await using WorkerTestContext context = CreateContext();

        int claimed = await context.Processor.ProcessBatchAsync(
            "worker-a",
            CancellationToken.None);

        Assert.Equal(1, claimed);

        await using CirrusDbContext dbContext = CreateDbContext();
        ArchiveObject archiveObject = await dbContext.ArchiveObjects
            .Include(item => item.Errors)
            .SingleAsync(item => item.ArchiveObjectId == archiveObjectId);
        ArchiveErrorQueueItem error = Assert.Single(archiveObject.Errors);

        Assert.True(error.Resolved);
        Assert.NotNull(error.ResolvedAt);
        Assert.Null(error.NextRetryAt);
        Assert.NotNull(archiveObject.LastIntegrityCheckAt);
        Assert.True(
            archiveObject.NextIntegrityCheckAt
            >= archiveObject.LastIntegrityCheckAt?.AddDays(7));
    }

    [PostgresFact]
    public async Task ProcessBatchAsync_RespectsMaximumConcurrency()
    {
        for (int index = 0; index < 4; index++)
        {
            await AddDueArchiveObjectAsync();
        }

        IntegrityCheckOptions options = WorkerTestContext.CreateOptions(
            batchSize: 4,
            maxConcurrentChecks: 2);
        await using WorkerTestContext context = CreateContext(options);
        context.ArchiveService.VerifyAsync = async (
            archiveObjectId,
            cancellationToken) =>
        {
            await Task.Delay(75, cancellationToken);
            return CreateIntegrityResult(archiveObjectId);
        };

        int claimed = await context.Processor.ProcessBatchAsync(
            "worker-a",
            CancellationToken.None);

        Assert.Equal(4, claimed);
        Assert.Equal(2, context.ArchiveService.MaxObservedConcurrency);
        Assert.Equal(4, context.ArchiveService.CallCounts.Count);
        Assert.All(
            context.ArchiveService.CallCounts.Values,
            count => Assert.Equal(1, count));
    }

    public Task InitializeAsync()
    {
        return fixture.ResetAndSeedAsync();
    }

    public Task DisposeAsync()
    {
        return fixture.ResetAndSeedAsync();
    }

    private WorkerTestContext CreateContext(
        IntegrityCheckOptions? options = null)
    {
        return WorkerTestContext.Create(
            fixture.GetRequiredConnectionString(),
            options);
    }

    private CirrusDbContext CreateDbContext()
    {
        return CoreTestFactory.CreateDbContext(
            fixture.GetRequiredConnectionString());
    }

    private async Task<long> AddDueArchiveObjectAsync(
        string? leaseOwner = null,
        DateTimeOffset? leaseUntil = null,
        bool addPendingRetry = false)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var archiveObject = new ArchiveObject
        {
            ObjectKey = $"worker-tests/{Guid.NewGuid():N}",
            BucketName = "cirrus-test",
            FileType = "worker-test",
            SourceSystem = "worker-suite",
            OriginalFilename = "payload.bin",
            Sha256Hash = new string('a', 64),
            SizeBytes = 7,
            ReceivedAt = now.AddDays(-1),
            ArchivedAt = now.AddDays(-1),
            RetentionUntil = DateOnly.FromDateTime(now.AddYears(1).Date),
            ArchiveStatus = ArchiveStatus.Active,
            IsWormProtected = false,
            NextIntegrityCheckAt = now.AddMinutes(-1),
            IntegrityCheckLeaseOwner = leaseOwner,
            IntegrityCheckLeaseUntil = leaseUntil,
            CreatedBy = "worker-suite"
        };

        if (addPendingRetry)
        {
            archiveObject.Errors.Add(new ArchiveErrorQueueItem
            {
                ErrorType = "INTEGRITY_CHECK_FAILED",
                ErrorTimestamp = now.AddMinutes(-10),
                RetryCount = 1,
                LastErrorMessage = "previous failure",
                NextRetryAt = now.AddMinutes(-1),
                Resolved = false
            });
        }

        await using CirrusDbContext dbContext = CreateDbContext();
        dbContext.ArchiveObjects.Add(archiveObject);
        await dbContext.SaveChangesAsync();
        return archiveObject.ArchiveObjectId;
    }

    private async Task MakeDueAgainAsync(long archiveObjectId)
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        await dbContext.ArchiveObjects
            .Where(item => item.ArchiveObjectId == archiveObjectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                item => item.NextIntegrityCheckAt,
                DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    private async Task<ArchiveObject> GetArchiveObjectAsync(
        long archiveObjectId)
    {
        await using CirrusDbContext dbContext = CreateDbContext();
        return await dbContext.ArchiveObjects
            .SingleAsync(item => item.ArchiveObjectId == archiveObjectId);
    }

    private static ArchiveIntegrityResult CreateIntegrityResult(
        long archiveObjectId)
    {
        string hash = new('a', 64);
        return new ArchiveIntegrityResult(
            archiveObjectId,
            true,
            hash,
            hash,
            7,
            7,
            DateTimeOffset.UtcNow);
    }

    private static int GetCallCount(
        WorkerTestContext firstWorker,
        WorkerTestContext secondWorker,
        long archiveObjectId)
    {
        firstWorker.ArchiveService.CallCounts.TryGetValue(
            archiveObjectId,
            out int firstCount);
        secondWorker.ArchiveService.CallCounts.TryGetValue(
            archiveObjectId,
            out int secondCount);
        return firstCount + secondCount;
    }
}
