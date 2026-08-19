using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;
using MTSM.Cirrus.Core.Enums;
using MTSM.Cirrus.Core.Security;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class MachineIdentityConfiguration : IEntityTypeConfiguration<MachineIdentity>
{
    public void Configure(EntityTypeBuilder<MachineIdentity> builder)
    {
        builder.ToTable("machine_identity", table =>
        {
            table.HasCheckConstraint("ck_machine_identity_status", "status IN ('Active', 'Disabled')");
            table.HasCheckConstraint("ck_machine_identity_disabled", "status <> 'Disabled' OR disabled_at IS NOT NULL");
        });
        builder.HasKey(x => x.MachineIdentityId);
        builder.HasAlternateKey(x => new { x.TenantId, x.MachineIdentityId });
        builder.Property(x => x.MachineIdentityId).ValueGeneratedOnAdd();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.DisabledAt).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MachineIdentityPermissionConfiguration : IEntityTypeConfiguration<MachineIdentityPermission>
{
    public void Configure(EntityTypeBuilder<MachineIdentityPermission> builder)
    {
        builder.ToTable("machine_identity_permission", table => table.HasCheckConstraint(
            "ck_machine_identity_permission_value",
            "permission IN ('ArchiveRead', 'ArchiveWrite', 'ArchiveDelete', 'ArchiveVerify')"));
        builder.HasKey(x => new { x.MachineIdentityId, x.Permission });
        builder.Property(x => x.Permission).HasConversion<string>().HasMaxLength(64).IsRequired();
        builder.HasOne(x => x.MachineIdentity).WithMany(x => x.Permissions)
            .HasForeignKey(x => x.MachineIdentityId).OnDelete(DeleteBehavior.Cascade);
    }
}
