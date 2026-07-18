using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class ArchiveErrorQueueItemConfiguration
    : IEntityTypeConfiguration<ArchiveErrorQueueItem>
{
    public void Configure(EntityTypeBuilder<ArchiveErrorQueueItem> builder)
    {
        builder.ToTable("archive_error_queue", t => t.HasCheckConstraint(
                    "ck_archive_error_queue_retry_count",
                    "retry_count >= 0"));

        builder.HasKey(x => x.ErrorId);

        builder.Property(x => x.ErrorId)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ErrorType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ErrorTimestamp)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .HasDefaultValue(0);

        builder.Property(x => x.LastErrorMessage)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.NextRetryAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.Resolved)
            .HasDefaultValue(false);

        builder.Property(x => x.ResolvedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(x => new
        {
            x.Resolved,
            x.NextRetryAt
        });

        builder.HasIndex(x => x.ArchiveObjectId);

        builder.HasOne(x => x.ArchiveObject)
            .WithMany(x => x.Errors)
            .HasForeignKey(x => x.ArchiveObjectId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}