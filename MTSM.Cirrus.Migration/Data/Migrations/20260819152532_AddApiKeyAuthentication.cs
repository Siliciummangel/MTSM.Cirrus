using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MTSM.Cirrus.Migration.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyAuthentication : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "machine_identity",
                schema: "cirrus",
                columns: table => new
                {
                    machine_identity_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_identity", x => x.machine_identity_id);
                    table.UniqueConstraint("ak_machine_identity_tenant_id_machine_identity_id", x => new { x.tenant_id, x.machine_identity_id });
                    table.CheckConstraint("ck_machine_identity_disabled", "status <> 'Disabled' OR disabled_at IS NOT NULL");
                    table.CheckConstraint("ck_machine_identity_status", "status IN ('Active', 'Disabled')");
                    table.ForeignKey(
                        name: "fk_machine_identity_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "cirrus",
                        principalTable: "tenant",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_key_credential",
                schema: "cirrus",
                columns: table => new
                {
                    api_key_credential_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    machine_identity_id = table.Column<long>(type: "bigint", nullable: false),
                    key_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", maxLength: 64, nullable: false),
                    hash_algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replaced_by_id = table.Column<long>(type: "bigint", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_key_credential", x => x.api_key_credential_id);
                    table.CheckConstraint("ck_api_key_credential_expiry", "expires_at IS NULL OR expires_at > created_at");
                    table.CheckConstraint("ck_api_key_credential_hash", "hash_algorithm = 'SHA-256' AND octet_length(secret_hash) = 32");
                    table.CheckConstraint("ck_api_key_credential_revoked", "status <> 'Revoked' OR revoked_at IS NOT NULL");
                    table.CheckConstraint("ck_api_key_credential_status", "status IN ('Active', 'Revoked')");
                    table.ForeignKey(
                        name: "fk_api_key_credential_api_key_credential_replaced_by_id",
                        column: x => x.replaced_by_id,
                        principalSchema: "cirrus",
                        principalTable: "api_key_credential",
                        principalColumn: "api_key_credential_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_key_credential_machine_identities_machine_identity_id",
                        column: x => x.machine_identity_id,
                        principalSchema: "cirrus",
                        principalTable: "machine_identity",
                        principalColumn: "machine_identity_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "machine_identity_permission",
                schema: "cirrus",
                columns: table => new
                {
                    machine_identity_id = table.Column<long>(type: "bigint", nullable: false),
                    permission = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_machine_identity_permission", x => new { x.machine_identity_id, x.permission });
                    table.CheckConstraint("ck_machine_identity_permission_value", "permission IN ('ArchiveRead', 'ArchiveWrite', 'ArchiveDelete', 'ArchiveVerify')");
                    table.ForeignKey(
                        name: "fk_machine_identity_permission_machine_identity_machine_identi",
                        column: x => x.machine_identity_id,
                        principalSchema: "cirrus",
                        principalTable: "machine_identity",
                        principalColumn: "machine_identity_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "security_audit_event",
                schema: "cirrus",
                columns: table => new
                {
                    security_audit_event_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    machine_identity_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    key_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    event_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_security_audit_event", x => x.security_audit_event_id);
                    table.ForeignKey(
                        name: "fk_security_audit_event_machine_identity_tenant_id_machine_ide",
                        columns: x => new { x.tenant_id, x.machine_identity_id },
                        principalSchema: "cirrus",
                        principalTable: "machine_identity",
                        principalColumns: new[] { "tenant_id", "machine_identity_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_security_audit_event_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "cirrus",
                        principalTable: "tenant",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_key_credential_key_id",
                schema: "cirrus",
                table: "api_key_credential",
                column: "key_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_key_credential_machine_identity_id_status",
                schema: "cirrus",
                table: "api_key_credential",
                columns: new[] { "machine_identity_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_api_key_credential_replaced_by_id",
                schema: "cirrus",
                table: "api_key_credential",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_machine_identity_tenant_id_name",
                schema: "cirrus",
                table: "machine_identity",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_machine_identity_tenant_id_status",
                schema: "cirrus",
                table: "machine_identity",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_security_audit_event_machine_identity_id_event_timestamp",
                schema: "cirrus",
                table: "security_audit_event",
                columns: new[] { "machine_identity_id", "event_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_security_audit_event_tenant_id_event_timestamp",
                schema: "cirrus",
                table: "security_audit_event",
                columns: new[] { "tenant_id", "event_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_security_audit_event_tenant_id_machine_identity_id",
                schema: "cirrus",
                table: "security_audit_event",
                columns: new[] { "tenant_id", "machine_identity_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_key_credential",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "machine_identity_permission",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "security_audit_event",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "machine_identity",
                schema: "cirrus");
        }
    }
}
