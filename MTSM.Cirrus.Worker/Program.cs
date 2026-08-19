using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Worker;

var builder = Host.CreateApplicationBuilder(args);

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
builder.Services.AddSingleton<IntegrityCheckProcessor>();
builder.Services.AddSingleton<PurgeProcessor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
