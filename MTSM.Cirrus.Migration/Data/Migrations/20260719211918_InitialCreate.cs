using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using System.Text.Json;

#nullable disable

namespace MTSM.Cirrus.Migration.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cirrus");

            migrationBuilder.CreateTable(
                name: "business_ref_type",
                schema: "cirrus",
                columns: table => new
                {
                    business_reference_type_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reference_type_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_ref_type", x => x.business_reference_type_id);
                });

            migrationBuilder.CreateTable(
                name: "retention_policy",
                schema: "cirrus",
                columns: table => new
                {
                    retention_policy_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    policy_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    retention_years = table.Column<int>(type: "integer", nullable: false),
                    delete_after_expiry = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    worm_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retention_policy", x => x.retention_policy_id);
                    table.CheckConstraint("ck_retention_policy_retention_years", "retention_years >= 0");
                });

            migrationBuilder.CreateTable(
                name: "archive_object",
                schema: "cirrus",
                columns: table => new
                {
                    archive_object_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    bucket_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    source_system = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    partner = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    original_filename = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    sha256hash = table.Column<string>(type: "char(64)", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retention_until = table.Column<DateOnly>(type: "date", nullable: false),
                    retention_policy_id = table.Column<int>(type: "integer", nullable: true),
                    archive_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    storage_version_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    encryption_key_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    is_worm_protected = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_object", x => x.archive_object_id);
                    table.CheckConstraint("ck_archive_object_size_bytes", "size_bytes >= 0");
                    table.ForeignKey(
                        name: "fk_archive_object_retention_policies_retention_policy_id",
                        column: x => x.retention_policy_id,
                        principalSchema: "cirrus",
                        principalTable: "retention_policy",
                        principalColumn: "retention_policy_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archive_business_ref",
                schema: "cirrus",
                columns: table => new
                {
                    archive_object_id = table.Column<long>(type: "bigint", nullable: false),
                    business_reference_type_id = table.Column<int>(type: "integer", nullable: false),
                    reference_value = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    business_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    tenant = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_business_ref", x => new { x.archive_object_id, x.business_reference_type_id, x.reference_value, x.business_type, x.tenant });
                    table.ForeignKey(
                        name: "fk_archive_business_ref_archive_objects_archive_object_id",
                        column: x => x.archive_object_id,
                        principalSchema: "cirrus",
                        principalTable: "archive_object",
                        principalColumn: "archive_object_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_archive_business_ref_business_reference_types_business_refe",
                        column: x => x.business_reference_type_id,
                        principalSchema: "cirrus",
                        principalTable: "business_ref_type",
                        principalColumn: "business_reference_type_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archive_error_queue",
                schema: "cirrus",
                columns: table => new
                {
                    error_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    archive_object_id = table.Column<long>(type: "bigint", nullable: true),
                    error_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    error_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error_message = table.Column<string>(type: "text", nullable: false),
                    next_retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_error_queue", x => x.error_id);
                    table.CheckConstraint("ck_archive_error_queue_retry_count", "retry_count >= 0");
                    table.ForeignKey(
                        name: "fk_archive_error_queue_archive_objects_archive_object_id",
                        column: x => x.archive_object_id,
                        principalSchema: "cirrus",
                        principalTable: "archive_object",
                        principalColumn: "archive_object_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "archive_event",
                schema: "cirrus",
                columns: table => new
                {
                    archive_event_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    archive_object_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    actor = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    details_json = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_event", x => x.archive_event_id);
                    table.ForeignKey(
                        name: "fk_archive_event_archive_objects_archive_object_id",
                        column: x => x.archive_object_id,
                        principalSchema: "cirrus",
                        principalTable: "archive_object",
                        principalColumn: "archive_object_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archive_business_ref_business_reference_type_id_reference_v",
                schema: "cirrus",
                table: "archive_business_ref",
                columns: new[] { "business_reference_type_id", "reference_value", "business_type", "tenant" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_business_ref_business_type_tenant",
                schema: "cirrus",
                table: "archive_business_ref",
                columns: new[] { "business_type", "tenant" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_business_ref_tenant_business_type_reference_value",
                schema: "cirrus",
                table: "archive_business_ref",
                columns: new[] { "tenant", "business_type", "reference_value" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_error_queue_archive_object_id",
                schema: "cirrus",
                table: "archive_error_queue",
                column: "archive_object_id");

            migrationBuilder.CreateIndex(
                name: "ix_archive_error_queue_resolved_next_retry_at",
                schema: "cirrus",
                table: "archive_error_queue",
                columns: new[] { "resolved", "next_retry_at" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_event_archive_object_id_event_timestamp",
                schema: "cirrus",
                table: "archive_event",
                columns: new[] { "archive_object_id", "event_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_event_event_type",
                schema: "cirrus",
                table: "archive_event",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_archive_status",
                schema: "cirrus",
                table: "archive_object",
                column: "archive_status");

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_archived_at",
                schema: "cirrus",
                table: "archive_object",
                column: "archived_at");

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_bucket_name_object_key",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "bucket_name", "object_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_retention_policy_id",
                schema: "cirrus",
                table: "archive_object",
                column: "retention_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_retention_until",
                schema: "cirrus",
                table: "archive_object",
                column: "retention_until");

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_sha256hash",
                schema: "cirrus",
                table: "archive_object",
                column: "sha256hash");

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_source_system_file_type_partner",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "source_system", "file_type", "partner" });

            migrationBuilder.CreateIndex(
                name: "ix_business_ref_type_reference_type_key",
                schema: "cirrus",
                table: "business_ref_type",
                column: "reference_type_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_retention_policy_policy_name",
                schema: "cirrus",
                table: "retention_policy",
                column: "policy_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archive_business_ref",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "archive_error_queue",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "archive_event",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "business_ref_type",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "archive_object",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "retention_policy",
                schema: "cirrus");
        }
    }
}
