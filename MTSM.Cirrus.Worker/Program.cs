using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Worker;
using MTSM.Cirrus.Worker.Maintenance;
using MTSM.Cirrus.Worker.StorageV2;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<StorageProcessingOptions>()
    .Bind(builder.Configuration.GetSection(
        StorageProcessingOptions.SectionName))
    .Validate(options => options.PollingIntervalSeconds > 0,
        "Storage-processing polling interval must be greater than zero.")
    .Validate(options => options.BatchSize is > 0 and <= 1000,
        "Storage-processing batch size must be between 1 and 1000.")
    .Validate(options => options.MaxConcurrency is > 0 and <= 100,
        "Storage-processing concurrency must be between 1 and 100.")
    .Validate(options => options.MaxConcurrency <= options.BatchSize,
        "Storage-processing concurrency must not exceed the batch size.")
    .Validate(options => options.LeaseDurationMinutes >= 3,
        "Storage-processing lease duration must be at least three minutes.")
    .Validate(options => options.InitialRetryDelaySeconds > 0,
        "Initial storage-processing retry delay must be positive.")
    .Validate(options => options.MaximumRetryDelayMinutes > 0,
        "Maximum storage-processing retry delay must be positive.")
    .Validate(options => options.MaximumAttempts > 0,
        "Maximum storage-processing attempts must be positive.")
    .Validate(options => options.MinimumChunkSizeBytes > 0
        && options.AverageChunkSizeBytes >= options.MinimumChunkSizeBytes
        && options.MaximumChunkSizeBytes >= options.AverageChunkSizeBytes,
        "Storage-processing chunk sizes are invalid.")
    .Validate(options => options.TargetPackSizeBytes >= options.MaximumChunkSizeBytes,
        "Target pack size must be at least the maximum chunk size.")
    .Validate(options => options.MaximumBatchWaitSeconds >= 0,
        "Maximum batch wait must not be negative.")
    .Validate(options => options.LeaseHeartbeatSeconds > 0
        && options.LeaseHeartbeatSeconds < options.LeaseDurationMinutes * 60,
        "Lease heartbeat must be positive and shorter than the lease duration.")
    .Validate(options => options.ZstdCompressionLevel is >= -5 and <= 22,
        "Zstd compression level must be between -5 and 22.")
    .Validate(options => options.PackMaintenanceBatchSize is > 0 and <= 1000,
        "Pack-maintenance batch size must be between 1 and 1000.")
    .Validate(options => options.CompactionUtilizationPercent is > 0 and < 100,
        "Compaction utilization must be between 1 and 99 percent.")
    .ValidateOnStart();

builder.Services
    .AddOptions<IntegrityCheckOptions>()
    .Bind(builder.Configuration.GetSection(
        IntegrityCheckOptions.SectionName))
    .Validate(options =>
        options.InitialVerificationDelayHours >= 0,
        "Initial verification delay must not be negative.")
    .Validate(options =>
        options.ReverificationIntervalDays > 0,
        "Reverification interval must be greater than zero.")
    .Validate(options =>
        options.FailureRetryDelayMinutes > 0,
        "Failure retry delay must be greater than zero.")
    .Validate(options =>
        options.PollingIntervalSeconds > 0,
        "Polling interval must be greater than zero.")
    .Validate(options =>
        options.BatchSize is > 0 and <= 1000,
        "Batch size must be between 1 and 1000.")
    .Validate(options =>
        options.MaxConcurrentChecks is > 0 and <= 100,
        "Maximum concurrency must be between 1 and 100.")
    .Validate(options =>
        options.MaxConcurrentChecks <= options.BatchSize,
        "Maximum concurrency must not exceed the batch size.")
    .Validate(options =>
        options.LeaseDurationMinutes >= 3,
        "Lease duration must be at least three minutes.")
    .Validate(options =>
        options.WorkerInstanceId is null
        || options.WorkerInstanceId.Trim().Length is > 0 and <= 180,
        "Worker instance ID must contain between 1 and 180 characters.")
    .ValidateOnStart();

builder.Services
    .AddOptions<PurgeOptions>()
    .Bind(builder.Configuration.GetSection(PurgeOptions.SectionName))
    .Validate(options => options.PollingIntervalSeconds > 0,
        "Purge polling interval must be greater than zero.")
    .Validate(options => options.BatchSize is > 0 and <= 1000,
        "Purge batch size must be between 1 and 1000.")
    .Validate(options => options.MaxConcurrentDeletes is > 0 and <= 100,
        "Purge concurrency must be between 1 and 100.")
    .Validate(options => options.MaxConcurrentDeletes <= options.BatchSize,
        "Purge concurrency must not exceed the batch size.")
    .Validate(options => options.LeaseDurationMinutes >= 3,
        "Purge lease duration must be at least three minutes.")
    .Validate(options => options.InitialRetryDelayMinutes > 0,
        "Initial purge retry delay must be greater than zero.")
    .Validate(options => options.MaximumRetryDelayMinutes >= options.InitialRetryDelayMinutes,
        "Maximum purge retry delay must not be smaller than the initial delay.")
    .ValidateOnStart();

string connectionString =
    builder.Configuration.GetConnectionString("ArchiveDatabase")
    ?? throw new InvalidOperationException(
        "The connection string 'ArchiveDatabase' is missing.");

builder.Services.AddCirrusDatabase(connectionString);
builder.Services.AddCirrusCore(builder.Configuration);
builder.Services.AddSingleton<StorageProcessingProcessor>();
builder.Services.AddSingleton<IContentChunker, FastCdcContentChunker>();
builder.Services.AddSingleton<StoragePackingLeaseManager>();
builder.Services.AddSingleton<ManifestCommitter>();
builder.Services.AddSingleton<PackWriter>();
builder.Services.AddSingleton<ArchivePackPlanner>();
builder.Services.AddSingleton<StagingFinalizer>();
builder.Services.AddSingleton<StoragePackingProcessor>();
builder.Services.AddSingleton<PackMaintenanceProcessor>();
builder.Services.AddSingleton<UnreachableContentCollector>();
builder.Services.AddSingleton<PackGarbageCollector>();
builder.Services.AddSingleton<PackMaintenanceLeaseManager>();
builder.Services.AddSingleton<PackCompactor>();
builder.Services.AddSingleton<IntegrityCheckProcessor>();
builder.Services.AddSingleton<PurgeProcessor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
