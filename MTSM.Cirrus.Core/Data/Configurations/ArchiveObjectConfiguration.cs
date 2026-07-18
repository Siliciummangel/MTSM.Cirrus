using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class ArchiveObjectConfiguration
    : IEntityTypeConfiguration<ArchiveObject>
{
    public void Configure(EntityTypeBuilder<ArchiveObject> builder)
    {
        builder.ToTable("archive_object", t => t.HasCheckConstraint(
                    "ck_archive_object_size_bytes",
                    "size_bytes >= 0"));

        builder.HasKey(x => x.ArchiveObjectId);

        builder.Property(x => x.ArchiveObjectId)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ObjectKey)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(x => x.BucketName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.FileType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.MimeType)
            .HasMaxLength(255);

        builder.Property(x => x.SourceSystem)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Partner)
            .HasMaxLength(255);

        builder.Property(x => x.OriginalFilename)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(x => x.Sha256Hash)
            .HasColumnType("char(64)");

        builder.Property(x => x.ReceivedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.ArchivedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.RetentionUntil)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.ArchiveStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(ArchiveStatus.Pending)
            .IsRequired();

        builder.Property(x => x.StorageVersionId)
            .HasMaxLength(1024);

        builder.Property(x => x.EncryptionKeyId)
            .HasMaxLength(1024);

        builder.Property(x => x.IsWormProtected)
            .HasDefaultValue(false);

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(x => new { x.BucketName, x.ObjectKey })
            .IsUnique();

        builder.HasIndex(x => x.Sha256Hash);

        builder.HasIndex(x => x.ArchivedAt);

        builder.HasIndex(x => x.RetentionUntil);

        builder.HasIndex(x => x.ArchiveStatus);

        builder.HasIndex(x => new
        {
            x.SourceSystem,
            x.FileType,
            x.Partner
        });

        builder.HasOne(x => x.RetentionPolicy)
            .WithMany(x => x.ArchiveObjects)
            .HasForeignKey(x => x.RetentionPolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}