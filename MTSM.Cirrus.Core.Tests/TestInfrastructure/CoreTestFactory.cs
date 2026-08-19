using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Services;

namespace MTSM.Cirrus.Core.Tests.TestInfrastructure;

internal static class CoreTestFactory
{
    public static CirrusDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CirrusDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(
                        "MTSM.Cirrus.Migration");

                    npgsql.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        "cirrus");
                })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CirrusDbContext(options);
    }

    public static ArchiveService CreateService(
        CirrusDbContext dbContext,
        InMemoryObjectStorage storage)
    {
        return new ArchiveService(
            dbContext,
            storage,
            NullLogger<ArchiveService>.Instance);
    }
}
