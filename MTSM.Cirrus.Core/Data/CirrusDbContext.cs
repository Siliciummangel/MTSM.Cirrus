using Microsoft.EntityFrameworkCore;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data;

public sealed class CirrusDbContext : DbContext
{
    public CirrusDbContext(DbContextOptions<CirrusDbContext> options)
        : base(options)
    {
    }

    public DbSet<ArchiveObject> ArchiveObjects =>
        Set<ArchiveObject>();

    public DbSet<BusinessReferenceType> BusinessReferenceTypes =>
        Set<BusinessReferenceType>();

    public DbSet<ArchiveBusinessReference> ArchiveBusinessReferences =>
        Set<ArchiveBusinessReference>();

    public DbSet<RetentionPolicy> RetentionPolicies =>
        Set<RetentionPolicy>();

    public DbSet<ArchiveEvent> ArchiveEvents =>
        Set<ArchiveEvent>();

    public DbSet<ArchiveErrorQueueItem> ArchiveErrorQueue =>
        Set<ArchiveErrorQueueItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("cirrus");

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CirrusDbContext).Assembly);
    }
}