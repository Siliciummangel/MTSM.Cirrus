using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MTSM.Cirrus.Core.Config;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Services;

namespace MTSM.Cirrus.Core.Tests.TestInfrastructure;

internal static class CoreTestFactory
{
    public static CirrusDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CirrusDbContext>()
            .UseNpgsql(connectionString)
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
            Options.Create(new ArchiveOptions
            {
                BucketName = "cirrus-test",
                ObjectKeyPrefix = "objects"
            }),
            NullLogger<ArchiveService>.Instance);
    }
}
