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

string connectionString =
    builder.Configuration.GetConnectionString("ArchiveDatabase")
    ?? throw new InvalidOperationException(
        "The connection string 'ArchiveDatabase' is missing.");

builder.Services.AddCirrusDatabase(connectionString);
builder.Services.AddCirrusCore(builder.Configuration);
builder.Services.AddSingleton<IntegrityCheckProcessor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
