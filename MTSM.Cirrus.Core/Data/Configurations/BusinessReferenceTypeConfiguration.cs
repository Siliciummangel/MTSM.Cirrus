using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;
namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class BusinessReferenceTypeConfiguration
    : IEntityTypeConfiguration<BusinessReferenceType>
{
    public void Configure(EntityTypeBuilder<BusinessReferenceType> builder)
    {
        builder.ToTable("business_ref_type");

        builder.HasKey(x => x.BusinessReferenceTypeId);

        builder.Property(x => x.BusinessReferenceTypeId)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.ReferenceTypeKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(1000);

        builder.HasIndex(x => x.ReferenceTypeKey)
            .IsUnique();
    }
}