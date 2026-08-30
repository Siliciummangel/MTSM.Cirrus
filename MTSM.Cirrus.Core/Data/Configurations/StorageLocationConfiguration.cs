using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class StorageLocationConfiguration : IEntityTypeConfiguration<StorageLocation>
{
    public void Configure(EntityTypeBuilder<StorageLocation> builder)
    {
        builder.ToTable("storage_location", table =>
        {
            table.HasCheckConstraint("ck_storage_location_offset", "pack_offset >= 0");
            table.HasCheckConstraint("ck_storage_location_lengths", "stored_length > 0 AND raw_length > 0");
        });
        builder.HasKey(x => x.StorageLocationId);
        builder.Property(x => x.CompressionAlgorithm).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.ContentChunkId);
        builder.HasIndex(x => new { x.StoragePackId, x.PackOffset }).IsUnique();
        builder.HasOne(x => x.ContentChunk).WithMany(x => x.StorageLocations)
            .HasForeignKey(x => x.ContentChunkId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StoragePack).WithMany(x => x.StorageLocations)
            .HasForeignKey(x => x.StoragePackId).OnDelete(DeleteBehavior.Restrict);
    }
}
