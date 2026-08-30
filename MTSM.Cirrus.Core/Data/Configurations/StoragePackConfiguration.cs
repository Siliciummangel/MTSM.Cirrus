using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class StoragePackConfiguration : IEntityTypeConfiguration<StoragePack>
{
    public void Configure(EntityTypeBuilder<StoragePack> builder)
    {
        builder.ToTable("storage_pack", table =>
            table.HasCheckConstraint("ck_storage_pack_size", "size_bytes >= 0"));
        builder.HasKey(x => x.StoragePackId);
        builder.Property(x => x.BucketName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.StorageVersionId).HasMaxLength(1024);
        builder.Property(x => x.HashAlgorithm).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PackHash).HasColumnType("char(64)");
        builder.Property(x => x.PackStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.UploadedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CommittedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.MaintenanceLeaseOwner).HasMaxLength(255);
        builder.Property(x => x.MaintenanceLeaseUntil).HasColumnType("timestamp with time zone");
        builder.Property(x => x.MaintenanceError).HasMaxLength(1024);
        builder.HasIndex(x => new { x.TenantId, x.BucketName, x.ObjectKey }).IsUnique();
        builder.HasIndex(x => new { x.PackStatus, x.CreatedAt });
        builder.HasIndex(x => x.MaintenanceLeaseUntil);
        builder.HasOne(x => x.Tenant).WithMany(x => x.StoragePacks)
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
