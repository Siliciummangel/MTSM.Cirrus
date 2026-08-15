using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MTSM.Cirrus.Core.Data;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();

string connectionString =
    builder.Configuration.GetConnectionString("ArchiveDatabase")
    ?? throw new InvalidOperationException(
        "Connection string 'ArchiveDatabase' was not configured.");

builder.Services.AddCirrusDatabase(connectionString);

using IHost host = builder.Build();

await using AsyncServiceScope scope =
    host.Services.CreateAsyncScope();

ILogger<Program> logger =
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

CirrusDbContext dbContext =
    scope.ServiceProvider.GetRequiredService<CirrusDbContext>();

logger.LogInformation(
    "Applying Cirrus database migrations...");

try
{
    await dbContext.Database.MigrateAsync(CancellationToken.None);

    logger.LogInformation(
        "Cirrus database migrations completed successfully.");
}
catch (Exception exception)
{
    logger.LogCritical(
        exception,
        "Applying Cirrus database migrations failed.");

    throw;
}
