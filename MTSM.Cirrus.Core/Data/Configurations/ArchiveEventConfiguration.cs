using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class ArchiveEventConfiguration
    : IEntityTypeConfiguration<ArchiveEvent>
{
    public void Configure(EntityTypeBuilder<ArchiveEvent> builder)
    {
        builder.ToTable("archive_event");

        builder.HasKey(x => x.ArchiveEventId);

        builder.Property(x => x.ArchiveEventId)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.EventType)
            .HasConversion<string>()
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.EventTimestamp)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.Actor)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.DetailsJson)
            .HasColumnType("jsonb");

        builder.HasIndex(x => new
        {
            x.ArchiveObjectId,
            x.EventTimestamp
        });

        builder.HasIndex(x => x.EventType);

        builder.HasOne(x => x.ArchiveObject)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.ArchiveObjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}