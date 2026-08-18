using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using Npgsql;
using Xunit;

namespace MTSM.Cirrus.Core.Tests.TestInfrastructure;

internal static class PostgresTestConfiguration
{
    public const string EnvironmentVariable = "CIRRUS_TEST_POSTGRES";

    public static string? TryGetConnectionString()
    {
        string? connectionString =
            Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        string databaseName = builder.Database ?? string.Empty;
        bool isClearlyTestDatabase =
            string.Equals(databaseName, "test", StringComparison.OrdinalIgnoreCase)
            || databaseName.StartsWith("test_", StringComparison.OrdinalIgnoreCase)
            || databaseName.EndsWith("_test", StringComparison.OrdinalIgnoreCase);

        if (!isClearlyTestDatabase)
        {
            return null;
        }

        return builder.ConnectionString;
    }
}

internal sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        bool isContinuousIntegration = string.Equals(
            Environment.GetEnvironmentVariable("CI"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!isContinuousIntegration
            && PostgresTestConfiguration.TryGetConnectionString() is null)
        {
            Skip = $"Set {PostgresTestConfiguration.EnvironmentVariable} to a PostgreSQL database named test, test_* or *_test.";
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL Core integration";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    public string? ConnectionString { get; } =
        PostgresTestConfiguration.TryGetConnectionString();

    public async Task InitializeAsync()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using CirrusDbContext dbContext =
            CoreTestFactory.CreateDbContext(ConnectionString);

        await dbContext.Database.ExecuteSqlRawAsync(
            "DROP SCHEMA IF EXISTS cirrus CASCADE;");
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.BusinessReferenceTypes.AddRange(
            new BusinessReferenceType
            {
                ReferenceTypeKey = "document-id"
            },
            new BusinessReferenceType
            {
                ReferenceTypeKey = "case-id"
            });

        dbContext.RetentionPolicies.Add(new RetentionPolicy
        {
            PolicyName = "ten-years",
            RetentionYears = 10,
            DeleteAfterExpiry = false,
            WormRequired = false
        });

        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (ConnectionString is null)
        {
            return;
        }

        await using CirrusDbContext dbContext =
            CoreTestFactory.CreateDbContext(ConnectionString);

        await dbContext.Database.ExecuteSqlRawAsync(
            "DROP SCHEMA IF EXISTS cirrus CASCADE;");
    }

    public string GetRequiredConnectionString()
    {
        return ConnectionString
            ?? throw new InvalidOperationException(
                "The PostgreSQL integration fixture is not configured.");
    }

    public async Task ResetCirrusSchemaAsync()
    {
        await using CirrusDbContext dbContext =
            CoreTestFactory.CreateDbContext(
                GetRequiredConnectionString());

        await dbContext.Database.ExecuteSqlRawAsync(
            "DROP SCHEMA IF EXISTS cirrus CASCADE;");
    }
}
