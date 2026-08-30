using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class ManifestChunkConfiguration : IEntityTypeConfiguration<ManifestChunk>
{
    public void Configure(EntityTypeBuilder<ManifestChunk> builder)
    {
        builder.ToTable("manifest_chunk", table =>
        {
            table.HasCheckConstraint("ck_manifest_chunk_sequence", "sequence_number >= 0");
            table.HasCheckConstraint("ck_manifest_chunk_offset", "original_offset >= 0");
            table.HasCheckConstraint("ck_manifest_chunk_length", "raw_length > 0");
        });
        builder.HasKey(x => new { x.ContentManifestId, x.SequenceNumber });
        builder.HasIndex(x => x.ContentChunkId);
        builder.HasOne(x => x.ContentManifest).WithMany(x => x.Chunks)
            .HasForeignKey(x => x.ContentManifestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ContentChunk).WithMany(x => x.ManifestChunks)
            .HasForeignKey(x => x.ContentChunkId).OnDelete(DeleteBehavior.Restrict);
    }
}
