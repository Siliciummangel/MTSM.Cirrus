using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MTSM.Cirrus.Core.Data;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Tests.TestInfrastructure;

namespace MTSM.Cirrus.Core.Tests;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseMigrationTests(PostgresFixture fixture)
    : IAsyncLifetime
{
    // Historical pre-release schema used to verify data-preserving upgrades.
    // After v0.1.0, update this when the supported upgrade baseline changes.
    private const string PreviousDatabaseVersion =
        "20260725225040_AddArchiveDeletionLifecycle";

    [PostgresFact]
    public async Task MigrateAsync_EmptyDatabaseAppliesAllMigrations()
    {
        await fixture.ResetCirrusSchemaAsync();

        await using CirrusDbContext dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync();

        string[] availableMigrations =
            [.. dbContext.Database.GetMigrations()];
        string[] appliedMigrations =
            [.. await dbContext.Database.GetAppliedMigrationsAsync()];
        string[] pendingMigrations =
            [.. await dbContext.Database.GetPendingMigrationsAsync()];

        Assert.NotEmpty(availableMigrations);
        Assert.Equal(availableMigrations, appliedMigrations);
        Assert.Empty(pendingMigrations);

        Assert.False(await dbContext.ArchiveObjects.AnyAsync());
        Assert.False(await dbContext.ArchiveEvents.AnyAsync());
    }

    [PostgresFact]
    public async Task MigrateAsync_PreviousVersionPreservesDataAndAppliesCurrentMigration()
    {
        await fixture.ResetCirrusSchemaAsync();

        await using CirrusDbContext dbContext = CreateDbContext();
        IMigrator migrator = dbContext.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousDatabaseVersion);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO cirrus.business_ref_type (reference_type_key)
            VALUES ('migration-object-id');

            WITH inserted_archive AS (
            INSERT INTO cirrus.archive_object
            (
                object_key,
                bucket_name,
                file_type,
                source_system,
                original_filename,
                size_bytes,
                received_at,
                retention_until,
                archive_status,
                is_worm_protected,
                created_by
            )
            VALUES
            (
                'objects/tenant-a/migration-test/2026/08/18/upgrade',
                'cirrus-test',
                'migration-test',
                'migration-suite',
                'upgrade.txt',
                42,
                TIMESTAMPTZ '2026-08-18 00:00:00+00',
                DATE '2036-08-18',
                'Active',
                FALSE,
                'migration-suite'
            )
            RETURNING archive_object_id
            )
            INSERT INTO cirrus.archive_business_ref
            (
                archive_object_id,
                business_reference_type_id,
                reference_value,
                business_type,
                tenant,
                created_at
            )
            SELECT inserted_archive.archive_object_id,
                   reference_type.business_reference_type_id,
                   'upgrade-object',
                   'migration-test',
                   'tenant-a',
                   CURRENT_TIMESTAMP
            FROM inserted_archive
            CROSS JOIN cirrus.business_ref_type AS reference_type
            WHERE reference_type.reference_type_key = 'migration-object-id';
            """);

        await dbContext.Database.MigrateAsync();
        dbContext.ChangeTracker.Clear();

        ArchiveObject archiveObject = await dbContext.ArchiveObjects
            .SingleAsync(item => item.ObjectKey ==
                "objects/tenant-a/migration-test/2026/08/18/upgrade");

        Assert.Equal("upgrade.txt", archiveObject.OriginalFilename);
        Assert.Equal(42, archiveObject.SizeBytes);
        Assert.True(archiveObject.TenantId > 0);
        Assert.Null(archiveObject.LastIntegrityCheckAt);
        Assert.Null(archiveObject.NextIntegrityCheckAt);
        Assert.Null(archiveObject.IntegrityCheckLeaseOwner);
        Assert.Null(archiveObject.IntegrityCheckLeaseUntil);
        Assert.Null(archiveObject.PurgeLeaseOwner);
        Assert.Null(archiveObject.PurgeLeaseUntil);
        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
    }

    private CirrusDbContext CreateDbContext()
    {
        return CoreTestFactory.CreateDbContext(
            fixture.GetRequiredConnectionString());
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return fixture.ResetAndSeedAsync();
    }
}
