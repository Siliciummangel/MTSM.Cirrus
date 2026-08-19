using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class SecurityAuditEventConfiguration : IEntityTypeConfiguration<SecurityAuditEvent>
{
    public void Configure(EntityTypeBuilder<SecurityAuditEvent> builder)
    {
        builder.ToTable("security_audit_event");
        builder.HasKey(x => x.SecurityAuditEventId);
        builder.Property(x => x.SecurityAuditEventId).ValueGeneratedOnAdd();
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Actor).HasMaxLength(255).IsRequired();
        builder.Property(x => x.KeyId).HasMaxLength(32);
        builder.Property(x => x.EventTimestamp).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.Details).HasMaxLength(2000);
        builder.HasIndex(x => new { x.TenantId, x.EventTimestamp });
        builder.HasIndex(x => new { x.MachineIdentityId, x.EventTimestamp });
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MachineIdentity).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.MachineIdentityId })
            .HasPrincipalKey(x => new { x.TenantId, x.MachineIdentityId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
