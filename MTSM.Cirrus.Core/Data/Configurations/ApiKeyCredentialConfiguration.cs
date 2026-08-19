using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTSM.Cirrus.Core.Entities;

namespace MTSM.Cirrus.Core.Data.Configurations;

public sealed class ApiKeyCredentialConfiguration : IEntityTypeConfiguration<ApiKeyCredential>
{
    public void Configure(EntityTypeBuilder<ApiKeyCredential> builder)
    {
        builder.ToTable("api_key_credential", table =>
        {
            table.HasCheckConstraint("ck_api_key_credential_status", "status IN ('Active', 'Revoked')");
            table.HasCheckConstraint("ck_api_key_credential_hash", "hash_algorithm = 'SHA-256' AND octet_length(secret_hash) = 32");
            table.HasCheckConstraint("ck_api_key_credential_revoked", "status <> 'Revoked' OR revoked_at IS NOT NULL");
            table.HasCheckConstraint("ck_api_key_credential_expiry", "expires_at IS NULL OR expires_at > created_at");
        });
        builder.HasKey(x => x.ApiKeyCredentialId);
        builder.Property(x => x.ApiKeyCredentialId).ValueGeneratedOnAdd();
        builder.Property(x => x.KeyId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SecretHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.HashAlgorithm).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.LastUsedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.RevokedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasIndex(x => x.KeyId).IsUnique();
        builder.HasIndex(x => new { x.MachineIdentityId, x.Status });
        builder.HasOne(x => x.MachineIdentity).WithMany(x => x.ApiKeyCredentials)
            .HasForeignKey(x => x.MachineIdentityId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ReplacedBy).WithMany().HasForeignKey(x => x.ReplacedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
