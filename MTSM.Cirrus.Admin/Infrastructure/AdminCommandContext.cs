using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MTSM.Cirrus.Core.Data;

namespace MTSM.Cirrus.Admin.Infrastructure;

public sealed class AdminCommandContext(IConfiguration configuration)
{
    public string Actor => string.IsNullOrWhiteSpace(Environment.UserName)
        ? "admin-cli:unknown"
        : $"admin-cli:{Environment.UserName}";

    public async Task<int> ExecuteAsync(
        Func<CirrusDbContext, string, CancellationToken, Task<int>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            string connectionString =
                configuration.GetConnectionString("ArchiveDatabase")
                ?? throw new InvalidOperationException(
                    "Connection string 'ArchiveDatabase' was not configured.");

            await using ServiceProvider services =
                new ServiceCollection()
                    .AddCirrusDatabase(connectionString)
                    .BuildServiceProvider();

            await using AsyncServiceScope scope = services.CreateAsyncScope();
            CirrusDbContext dbContext = scope.ServiceProvider.GetRequiredService<CirrusDbContext>();
            return await action(dbContext, Actor, cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return CliExitCodes.Error;
        }
    }
}
