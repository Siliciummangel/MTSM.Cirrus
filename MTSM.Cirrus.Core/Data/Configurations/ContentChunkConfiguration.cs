using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class ContentChunkConfiguration : IEntityTypeConfiguration<ContentChunk>
{
    public void Configure(EntityTypeBuilder<ContentChunk> builder)
    {
        builder.ToTable("content_chunk", table =>
            table.HasCheckConstraint("ck_content_chunk_raw_size", "raw_size_bytes > 0"));
        builder.HasKey(x => x.ContentChunkId);
        builder.Property(x => x.HashAlgorithm).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ChunkHash).HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.TenantId, x.HashAlgorithm, x.ChunkHash }).IsUnique();
        builder.HasOne(x => x.Tenant).WithMany(x => x.ContentChunks)
            .HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
