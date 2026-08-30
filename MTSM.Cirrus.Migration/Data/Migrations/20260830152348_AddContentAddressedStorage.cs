using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MTSM.Cirrus.Migration.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentAddressedStorage : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "content_manifest_id",
                schema: "cirrus",
                table: "archive_object",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "content_chunk",
                schema: "cirrus",
                columns: table => new
                {
                    content_chunk_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    hash_algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    chunk_hash = table.Column<string>(type: "char(64)", nullable: false),
                    raw_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_chunk", x => x.content_chunk_id);
                    table.CheckConstraint("ck_content_chunk_raw_size", "raw_size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_content_chunk_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "cirrus",
                        principalTable: "tenant",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "content_manifest",
                schema: "cirrus",
                columns: table => new
                {
                    content_manifest_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    manifest_format_version = table.Column<int>(type: "integer", nullable: false),
                    hash_algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    original_hash = table.Column<string>(type: "char(64)", nullable: false),
                    original_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    chunking_algorithm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    chunking_algorithm_version = table.Column<int>(type: "integer", nullable: false),
                    minimum_chunk_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    average_chunk_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    maximum_chunk_size_bytes = table.Column<int>(type: "integer", nullable: false),
                    chunk_count = table.Column<int>(type: "integer", nullable: false),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_manifest", x => x.content_manifest_id);
                    table.UniqueConstraint("ak_content_manifests_tenant_id_content_manifest_id", x => new { x.tenant_id, x.content_manifest_id });
                    table.CheckConstraint("ck_content_manifest_chunk_count", "chunk_count > 0");
                    table.CheckConstraint("ck_content_manifest_chunk_sizes", "minimum_chunk_size_bytes > 0 AND average_chunk_size_bytes >= minimum_chunk_size_bytes AND maximum_chunk_size_bytes >= average_chunk_size_bytes");
                    table.CheckConstraint("ck_content_manifest_original_size", "original_size_bytes >= 0");
                    table.ForeignKey(
                        name: "fk_content_manifest_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "cirrus",
                        principalTable: "tenant",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "storage_pack",
                schema: "cirrus",
                columns: table => new
                {
                    storage_pack_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<long>(type: "bigint", nullable: false),
                    bucket_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    storage_version_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    pack_format_version = table.Column<int>(type: "integer", nullable: false),
                    hash_algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    pack_hash = table.Column<string>(type: "char(64)", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    pack_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    maintenance_lease_owner = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    maintenance_lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    maintenance_attempts = table.Column<int>(type: "integer", nullable: false),
                    maintenance_error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_pack", x => x.storage_pack_id);
                    table.CheckConstraint("ck_storage_pack_size", "size_bytes >= 0");
                    table.ForeignKey(
                        name: "fk_storage_pack_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "cirrus",
                        principalTable: "tenant",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "manifest_chunk",
                schema: "cirrus",
                columns: table => new
                {
                    content_manifest_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    content_chunk_id = table.Column<long>(type: "bigint", nullable: false),
                    original_offset = table.Column<long>(type: "bigint", nullable: false),
                    raw_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manifest_chunk", x => new { x.content_manifest_id, x.sequence_number });
                    table.CheckConstraint("ck_manifest_chunk_length", "raw_length > 0");
                    table.CheckConstraint("ck_manifest_chunk_offset", "original_offset >= 0");
                    table.CheckConstraint("ck_manifest_chunk_sequence", "sequence_number >= 0");
                    table.ForeignKey(
                        name: "fk_manifest_chunk_content_chunk_content_chunk_id",
                        column: x => x.content_chunk_id,
                        principalSchema: "cirrus",
                        principalTable: "content_chunk",
                        principalColumn: "content_chunk_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_manifest_chunk_content_manifest_content_manifest_id",
                        column: x => x.content_manifest_id,
                        principalSchema: "cirrus",
                        principalTable: "content_manifest",
                        principalColumn: "content_manifest_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "storage_location",
                schema: "cirrus",
                columns: table => new
                {
                    storage_location_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    content_chunk_id = table.Column<long>(type: "bigint", nullable: false),
                    storage_pack_id = table.Column<long>(type: "bigint", nullable: false),
                    pack_offset = table.Column<long>(type: "bigint", nullable: false),
                    stored_length = table.Column<int>(type: "integer", nullable: false),
                    raw_length = table.Column<int>(type: "integer", nullable: false),
                    compression_algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    compression_version = table.Column<int>(type: "integer", nullable: false),
                    storage_format_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_location", x => x.storage_location_id);
                    table.CheckConstraint("ck_storage_location_lengths", "stored_length > 0 AND raw_length > 0");
                    table.CheckConstraint("ck_storage_location_offset", "pack_offset >= 0");
                    table.ForeignKey(
                        name: "fk_storage_location_content_chunk_content_chunk_id",
                        column: x => x.content_chunk_id,
                        principalSchema: "cirrus",
                        principalTable: "content_chunk",
                        principalColumn: "content_chunk_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_storage_location_storage_packs_storage_pack_id",
                        column: x => x.storage_pack_id,
                        principalSchema: "cirrus",
                        principalTable: "storage_pack",
                        principalColumn: "storage_pack_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_content_manifest_id",
                schema: "cirrus",
                table: "archive_object",
                column: "content_manifest_id");

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_tenant_id_content_manifest_id",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "tenant_id", "content_manifest_id" });

            migrationBuilder.CreateIndex(
                name: "ix_content_chunk_tenant_id_hash_algorithm_chunk_hash",
                schema: "cirrus",
                table: "content_chunk",
                columns: new[] { "tenant_id", "hash_algorithm", "chunk_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_manifest_tenant_id_original_hash",
                schema: "cirrus",
                table: "content_manifest",
                columns: new[] { "tenant_id", "original_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_manifest_chunk_content_chunk_id",
                schema: "cirrus",
                table: "manifest_chunk",
                column: "content_chunk_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_location_content_chunk_id",
                schema: "cirrus",
                table: "storage_location",
                column: "content_chunk_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_location_storage_pack_id_pack_offset",
                schema: "cirrus",
                table: "storage_location",
                columns: new[] { "storage_pack_id", "pack_offset" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_pack_maintenance_lease_until",
                schema: "cirrus",
                table: "storage_pack",
                column: "maintenance_lease_until");

            migrationBuilder.CreateIndex(
                name: "ix_storage_pack_pack_status_created_at",
                schema: "cirrus",
                table: "storage_pack",
                columns: new[] { "pack_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_storage_pack_tenant_id_bucket_name_object_key",
                schema: "cirrus",
                table: "storage_pack",
                columns: new[] { "tenant_id", "bucket_name", "object_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_archive_object_content_manifests_tenant_id_content_manifest",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "tenant_id", "content_manifest_id" },
                principalSchema: "cirrus",
                principalTable: "content_manifest",
                principalColumns: new[] { "tenant_id", "content_manifest_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_archive_object_content_manifests_tenant_id_content_manifest",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropTable(
                name: "manifest_chunk",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "storage_location",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "content_manifest",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "content_chunk",
                schema: "cirrus");

            migrationBuilder.DropTable(
                name: "storage_pack",
                schema: "cirrus");

            migrationBuilder.DropIndex(
                name: "ix_archive_object_content_manifest_id",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropIndex(
                name: "ix_archive_object_tenant_id_content_manifest_id",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "content_manifest_id",
                schema: "cirrus",
                table: "archive_object");
        }
    }
}
