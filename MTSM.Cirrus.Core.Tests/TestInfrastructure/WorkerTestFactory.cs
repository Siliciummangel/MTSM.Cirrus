using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Abstractions;
using MTSM.Cirrus.Core.Models;
using MTSM.Cirrus.Worker;
using System.Collections.Concurrent;

namespace MTSM.Cirrus.Core.Tests.TestInfrastructure;

internal sealed class WorkerTestContext : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    private WorkerTestContext(
        ServiceProvider serviceProvider,
        WorkerArchiveService archiveService,
        IntegrityCheckProcessor processor)
    {
        _serviceProvider = serviceProvider;
        ArchiveService = archiveService;
        Processor = processor;
    }

    public WorkerArchiveService ArchiveService { get; }

    public IntegrityCheckProcessor Processor { get; }

    public static WorkerTestContext Create(
        string connectionString,
        IntegrityCheckOptions? options = null)
    {
        var archiveService = new WorkerArchiveService();
        var services = new ServiceCollection();

        services.AddScoped(_ =>
            CoreTestFactory.CreateDbContext(connectionString));
        services.AddSingleton<IArchiveService>(archiveService);

        ServiceProvider serviceProvider = services.BuildServiceProvider();
        IntegrityCheckOptions configuredOptions = options ?? CreateOptions();

        var processor = new IntegrityCheckProcessor(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(configuredOptions),
            NullLogger<IntegrityCheckProcessor>.Instance);

        return new WorkerTestContext(
            serviceProvider,
            archiveService,
            processor);
    }

    public static IntegrityCheckOptions CreateOptions(
        int batchSize = 10,
        int maxConcurrentChecks = 2)
    {
        return new IntegrityCheckOptions
        {
            InitialVerificationDelayHours = 24,
            ReverificationIntervalDays = 7,
            FailureRetryDelayMinutes = 5,
            PollingIntervalSeconds = 1,
            BatchSize = batchSize,
            MaxConcurrentChecks = maxConcurrentChecks,
            LeaseDurationMinutes = 3,
            WorkerInstanceId = "test-worker"
        };
    }

    public ValueTask DisposeAsync()
    {
        return _serviceProvider.DisposeAsync();
    }
}

internal sealed class WorkerArchiveService : IArchiveService
{
    private int _activeCalls;
    private int _maxObservedConcurrency;

    public ConcurrentDictionary<long, int> CallCounts { get; } = new();

    public int MaxObservedConcurrency =>
        Volatile.Read(ref _maxObservedConcurrency);

    public Func<long, CancellationToken, Task<ArchiveIntegrityResult>>
        VerifyAsync { get; set; } = DefaultVerifyAsync;

    public async Task<ArchiveIntegrityResult> VerifyIntegrityAsync(
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        CallCounts.AddOrUpdate(archiveObjectId, 1, (_, count) => count + 1);

        int activeCalls = Interlocked.Increment(ref _activeCalls);
        UpdateMaximum(activeCalls);

        try
        {
            return await VerifyAsync(archiveObjectId, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
        }
    }

    private void UpdateMaximum(int candidate)
    {
        int current;

        do
        {
            current = Volatile.Read(ref _maxObservedConcurrency);

            if (candidate <= current)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(
            ref _maxObservedConcurrency,
            candidate,
            current) != current);
    }

    private static Task<ArchiveIntegrityResult> DefaultVerifyAsync(
        long archiveObjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string hash = new('a', 64);

        return Task.FromResult(new ArchiveIntegrityResult(
            archiveObjectId,
            true,
            hash,
            hash,
            7,
            7,
            DateTimeOffset.UtcNow));
    }

    public Task<ArchiveFileResult> ArchiveAsync(
        ArchiveFileRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ArchiveDownloadResult> DownloadAsync(
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ArchiveMetadataResult?> GetMetadataAsync(
        long tenantId,
        long archiveObjectId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ArchiveSearchResult> SearchAsync(
        long tenantId,
        ArchiveSearchRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ArchiveIntegrityStatusResult?> GetIntegrityStatusAsync(
        long tenantId,
        long archiveObjectId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ArchiveDeletionRequestResult> RequestDeletionAsync(
        long tenantId,
        long archiveObjectId,
        string actor,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class PurgeWorkerTestContext : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    private PurgeWorkerTestContext(
        ServiceProvider serviceProvider,
        PurgeProcessor processor,
        InMemoryObjectStorage storage,
        TestTimeProvider clock)
    {
        _serviceProvider = serviceProvider;
        Processor = processor;
        Storage = storage;
        Clock = clock;
    }

    public PurgeProcessor Processor { get; }
    public InMemoryObjectStorage Storage { get; }
    public TestTimeProvider Clock { get; }

    public static PurgeWorkerTestContext Create(
        string connectionString,
        DateTimeOffset now,
        PurgeOptions? options = null,
        InMemoryObjectStorage? storage = null)
    {
        storage ??= new InMemoryObjectStorage();
        var clock = new TestTimeProvider(now);
        var services = new ServiceCollection();
        services.AddScoped(_ => CoreTestFactory.CreateDbContext(connectionString));
        services.AddSingleton<IObjectStorage>(storage);
        ServiceProvider provider = services.BuildServiceProvider();
        PurgeOptions configured = options ?? new PurgeOptions
        {
            PollingIntervalSeconds = 1,
            BatchSize = 10,
            MaxConcurrentDeletes = 2,
            LeaseDurationMinutes = 3,
            InitialRetryDelayMinutes = 5,
            MaximumRetryDelayMinutes = 60
        };
        var processor = new PurgeProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(configured),
            clock,
            NullLogger<PurgeProcessor>.Instance);
        return new PurgeWorkerTestContext(provider, processor, storage, clock);
    }

    public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();
}

internal sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}

internal sealed class StorageWorkerTestContext : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    private StorageWorkerTestContext(
        ServiceProvider serviceProvider,
        StorageProcessingProcessor processor,
        InMemoryObjectStorage storage,
        TestTimeProvider clock)
    {
        _serviceProvider = serviceProvider;
        Processor = processor;
        Storage = storage;
        Clock = clock;
    }

    public StorageProcessingProcessor Processor { get; }
    public InMemoryObjectStorage Storage { get; }
    public TestTimeProvider Clock { get; }

    public static StorageWorkerTestContext Create(
        string connectionString,
        DateTimeOffset now,
        StorageProcessingOptions? options = null,
        InMemoryObjectStorage? storage = null)
    {
        storage ??= new InMemoryObjectStorage();
        var clock = new TestTimeProvider(now);
        var services = new ServiceCollection();
        services.AddScoped(_ => CoreTestFactory.CreateDbContext(connectionString));
        services.AddSingleton<IObjectStorage>(storage);
        ServiceProvider provider = services.BuildServiceProvider();
        StorageProcessingOptions configured = options ?? new StorageProcessingOptions
        {
            PollingIntervalSeconds = 1,
            BatchSize = 10,
            MaxConcurrency = 2,
            LeaseDurationMinutes = 3,
            InitialRetryDelaySeconds = 30,
            MaximumRetryDelayMinutes = 5,
            MaximumAttempts = 3
        };
        var processor = new StorageProcessingProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(configured),
            clock,
            NullLogger<StorageProcessingProcessor>.Instance);
        return new StorageWorkerTestContext(provider, processor, storage, clock);
    }

    public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();
}
