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
        builder.ToTable(
            "archive_object",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_archive_object_size_bytes",
                    "size_bytes >= 0");

                tableBuilder.HasCheckConstraint(
                    "ck_archive_object_deletion_requested",
                    """
                    archive_status <> 'DeletionRequested'
                    OR (
                        deletion_requested_at IS NOT NULL
                        AND deletion_requested_by IS NOT NULL
                    )
                    """);

                tableBuilder.HasCheckConstraint(
                    "ck_archive_object_purged",
                    """
                    archive_status <> 'Purged'
                    OR purged_at IS NOT NULL
                    """);
            });

        builder.HasKey(x => x.ArchiveObjectId);

        builder.HasAlternateKey(x => new
        {
            x.TenantId,
            x.ArchiveObjectId
        });

        builder.Property(x => x.ArchiveObjectId)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.TenantId)
            .IsRequired();

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

        builder.Property(x => x.DeletionRequestedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.DeletionRequestedBy)
            .HasMaxLength(255);

        builder.Property(x => x.PurgedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.PurgeLeaseOwner)
            .HasMaxLength(255);

        builder.Property(x => x.PurgeLeaseUntil)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.StorageVersionId)
            .HasMaxLength(1024);

        builder.Property(x => x.EncryptionKeyId)
            .HasMaxLength(1024);

        builder.Property(x => x.IsWormProtected)
            .HasDefaultValue(false);

        builder.Property(x => x.LastIntegrityCheckAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.NextIntegrityCheckAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.IntegrityCheckLeaseOwner)
            .HasMaxLength(255);

        builder.Property(x => x.IntegrityCheckLeaseUntil)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(x => new
        {
            x.TenantId,
            x.BucketName,
            x.ObjectKey
        })
        .IsUnique();

        builder.HasIndex(x => new { x.TenantId, x.ArchiveStatus });
        builder.HasIndex(x => new { x.TenantId, x.ReceivedAt });

        builder.HasIndex(x => x.Sha256Hash);

        builder.HasIndex(x => x.ArchivedAt);

        builder.HasIndex(x => x.RetentionUntil);

        builder.HasIndex(x => x.ArchiveStatus);

        builder.HasIndex(x => x.DeletionRequestedAt);

        builder.HasIndex(x => x.PurgedAt);

        builder.HasIndex(x => new
        {
            x.ArchiveStatus,
            x.NextIntegrityCheckAt,
            x.IntegrityCheckLeaseUntil
        });

        builder.HasIndex(x => new
        {
            x.ArchiveStatus,
            x.RetentionUntil,
            x.PurgeLeaseUntil
        });

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

        builder.HasOne(x => x.Tenant)
            .WithMany(x => x.ArchiveObjects)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
