using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class ArchiveBusinessReferenceConfiguration
    : IEntityTypeConfiguration<ArchiveBusinessReference>
{
    public void Configure(EntityTypeBuilder<ArchiveBusinessReference> builder)
    {
        builder.ToTable("archive_business_ref");

        builder.HasKey(x => new
        {
            x.ArchiveObjectId,
            x.BusinessReferenceTypeId,
            x.ReferenceValue,
            x.BusinessType,
            x.Tenant
        });

        builder.Property(x => x.ReferenceValue)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.BusinessType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Tenant)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasIndex(x => new
        {
            x.BusinessReferenceTypeId,
            x.ReferenceValue,
            x.BusinessType,
            x.Tenant
        });

        builder.HasIndex(x => new
        {
            x.BusinessType,
            x.Tenant
        });

        builder.HasIndex(x => new
        {
            x.Tenant,
            x.BusinessType,
            x.ReferenceValue
        });

        builder.HasOne(x => x.ArchiveObject)
            .WithMany(x => x.BusinessReferences)
            .HasForeignKey(x => x.ArchiveObjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.BusinessReferenceType)
            .WithMany(x => x.BusinessReferences)
            .HasForeignKey(x => x.BusinessReferenceTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}