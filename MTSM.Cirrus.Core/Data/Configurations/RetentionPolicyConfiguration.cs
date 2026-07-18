using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class RetentionPolicyConfiguration
    : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> builder)
    {
        builder.ToTable("retention_policy", t => t.HasCheckConstraint(
                    "ck_retention_policy_retention_years",
                    "retention_years >= 0"));

        builder.HasKey(x => x.RetentionPolicyId);

        builder.Property(x => x.RetentionPolicyId)
            .ValueGeneratedOnAdd();

        builder.Property(x => x.PolicyName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.RetentionYears)
            .IsRequired();

        builder.Property(x => x.DeleteAfterExpiry)
            .HasDefaultValue(false);

        builder.Property(x => x.WormRequired)
            .HasDefaultValue(false);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.HasIndex(x => x.PolicyName)
            .IsUnique();
    }
}