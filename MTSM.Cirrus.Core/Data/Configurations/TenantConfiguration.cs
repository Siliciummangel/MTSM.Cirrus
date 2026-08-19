using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenant");

        builder.HasKey(x => x.TenantId);

        builder.Property(x => x.TenantId)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(TenantStatus.Active)
            .IsRequired();

        builder.Property(x => x.BucketName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.ObjectKeyPrefix)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(x => x.EncryptionKeyId)
            .HasMaxLength(1024);

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.BucketName, x.ObjectKeyPrefix })
            .IsUnique();

        builder.HasOne(x => x.DefaultRetentionPolicy)
            .WithMany()
            .HasForeignKey(x => x.DefaultRetentionPolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
