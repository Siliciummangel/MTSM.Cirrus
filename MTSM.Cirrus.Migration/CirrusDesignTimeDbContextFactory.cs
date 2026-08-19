using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MTSM.Cirrus.Core.Data;

namespace MTSM.Cirrus.DesignTime;

public sealed class CirrusDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<CirrusDbContext>
{
    public CirrusDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__ArchiveDatabase")
            ?? "Host=localhost;Database=cirrus;Username=cirrus;Password=change-me";

        var options = new DbContextOptionsBuilder<CirrusDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(
                    typeof(CirrusDesignTimeDbContextFactory).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CirrusDbContext(options);
    }
}
