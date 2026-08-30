using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class ContentManifestConfiguration : IEntityTypeConfiguration<ContentManifest>
{
    public void Configure(EntityTypeBuilder<ContentManifest> builder)
    {
        builder.ToTable("content_manifest", table =>
        {
            table.HasCheckConstraint("ck_content_manifest_original_size", "original_size_bytes >= 0");
            table.HasCheckConstraint("ck_content_manifest_chunk_count", "chunk_count > 0");
            table.HasCheckConstraint("ck_content_manifest_chunk_sizes", "minimum_chunk_size_bytes > 0 AND average_chunk_size_bytes >= minimum_chunk_size_bytes AND maximum_chunk_size_bytes >= average_chunk_size_bytes");
        });
        builder.HasKey(x => x.ContentManifestId);
        builder.HasAlternateKey(x => new { x.TenantId, x.ContentManifestId });
        builder.Property(x => x.HashAlgorithm).HasMaxLength(32).IsRequired();
        builder.Property(x => x.OriginalHash).HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.ChunkingAlgorithm).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CommittedAt).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.TenantId, x.OriginalHash });
        builder.HasOne(x => x.Tenant).WithMany(x => x.ContentManifests)
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
