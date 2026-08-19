using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MTSM.Cirrus.Migration.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFirstClassTenancy : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant",
                schema: "cirrus",
                columns: table => new
                {
                    tenant_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Active"),
                    bucket_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    object_key_prefix = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    encryption_key_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    default_retention_policy_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant", x => x.tenant_id);
                    table.ForeignKey(
                        name: "fk_tenant_retention_policy_default_retention_policy_id",
                        column: x => x.default_retention_policy_id,
                        principalSchema: "cirrus",
                        principalTable: "retention_policy",
                        principalColumn: "retention_policy_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.DropForeignKey(
                name: "fk_archive_business_ref_archive_objects_archive_object_id",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.DropIndex(
                name: "ix_archive_object_bucket_name_object_key",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropPrimaryKey(
                name: "pk_archive_business_ref",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.DropIndex(
                name: "ix_archive_business_ref_business_reference_type_id_reference_v",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.DropIndex(
                name: "ix_archive_business_ref_business_type_tenant",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.DropIndex(
                name: "ix_archive_business_ref_tenant_business_type_reference_value",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.AddColumn<long>(
                name: "tenant_id",
                schema: "cirrus",
                table: "archive_object",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "tenant_id",
                schema: "cirrus",
                table: "archive_business_ref",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM cirrus.archive_object
                        WHERE array_length(string_to_array(object_key, '/'), 1) < 7
                           OR split_part(
                                object_key,
                                '/',
                                array_length(string_to_array(object_key, '/'), 1) - 5) = '')
                    THEN
                        RAISE EXCEPTION 'Tenant migration found an archive object key that does not match the Cirrus legacy key layout.';
                    END IF;
                END $$;

                INSERT INTO cirrus.tenant (
                    display_name, status, bucket_name, object_key_prefix, created_at)
                SELECT split_part(
                           archive_object.object_key,
                           '/',
                           array_length(string_to_array(archive_object.object_key, '/'), 1) - 5),
                       'Active',
                       min(archive_object.bucket_name),
                       'pending-migration',
                       CURRENT_TIMESTAMP
                FROM cirrus.archive_object AS archive_object
                GROUP BY split_part(
                    archive_object.object_key,
                    '/',
                    array_length(string_to_array(archive_object.object_key, '/'), 1) - 5);

                UPDATE cirrus.tenant
                SET object_key_prefix = 'tenants/' || tenant_id || '/objects';

                UPDATE cirrus.archive_object AS archive_object
                SET tenant_id = tenant.tenant_id
                FROM cirrus.tenant AS tenant
                WHERE tenant.display_name = split_part(
                    archive_object.object_key,
                    '/',
                    array_length(string_to_array(archive_object.object_key, '/'), 1) - 5);

                UPDATE cirrus.archive_business_ref AS reference
                SET tenant_id = archive_object.tenant_id
                FROM cirrus.archive_object AS archive_object
                WHERE archive_object.archive_object_id = reference.archive_object_id;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "tenant_id",
                schema: "cirrus",
                table: "archive_object",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "tenant_id",
                schema: "cirrus",
                table: "archive_business_ref",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "tenant",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_archive_objects_tenant_id_archive_object_id",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "tenant_id", "archive_object_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_archive_business_ref",
                schema: "cirrus",
                table: "archive_business_ref",
                columns: new[] { "tenant_id", "archive_object_id", "business_reference_type_id", "reference_value", "business_type" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_tenant_id_archive_status",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "tenant_id", "archive_status" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_tenant_id_bucket_name_object_key",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "tenant_id", "bucket_name", "object_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_tenant_id_received_at",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "tenant_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_business_ref_business_reference_type_id",
                schema: "cirrus",
                table: "archive_business_ref",
                column: "business_reference_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_archive_business_ref_tenant_id_business_reference_type_id_r",
                schema: "cirrus",
                table: "archive_business_ref",
                columns: new[] { "tenant_id", "business_reference_type_id", "reference_value", "business_type" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_business_ref_tenant_id_business_type_reference_value",
                schema: "cirrus",
                table: "archive_business_ref",
                columns: new[] { "tenant_id", "business_type", "reference_value" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_bucket_name_object_key_prefix",
                schema: "cirrus",
                table: "tenant",
                columns: new[] { "bucket_name", "object_key_prefix" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_default_retention_policy_id",
                schema: "cirrus",
                table: "tenant",
                column: "default_retention_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_status",
                schema: "cirrus",
                table: "tenant",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "fk_archive_business_ref_archive_objects_tenant_id_archive_obje",
                schema: "cirrus",
                table: "archive_business_ref",
                columns: new[] { "tenant_id", "archive_object_id" },
                principalSchema: "cirrus",
                principalTable: "archive_object",
                principalColumns: new[] { "tenant_id", "archive_object_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_archive_object_tenants_tenant_id",
                schema: "cirrus",
                table: "archive_object",
                column: "tenant_id",
                principalSchema: "cirrus",
                principalTable: "tenant",
                principalColumn: "tenant_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_archive_business_ref_archive_objects_tenant_id_archive_obje",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.DropForeignKey(
                name: "fk_archive_object_tenants_tenant_id",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropTable(
                name: "tenant",
                schema: "cirrus");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_archive_objects_tenant_id_archive_object_id",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropIndex(
                name: "ix_archive_object_tenant_id_archive_status",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropIndex(
                name: "ix_archive_object_tenant_id_bucket_name_object_key",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropIndex(
                name: "ix_archive_object_tenant_id_received_at",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropPrimaryKey(
                name: "pk_archive_business_ref",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.DropIndex(
                name: "ix_archive_business_ref_business_reference_type_id",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.DropIndex(
                name: "ix_archive_business_ref_tenant_id_business_reference_type_id_r",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.DropIndex(
                name: "ix_archive_business_ref_tenant_id_business_type_reference_value",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "cirrus",
                table: "archive_business_ref");

            migrationBuilder.AddColumn<string>(
                name: "tenant",
                schema: "cirrus",
                table: "archive_business_ref",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "pk_archive_business_ref",
                schema: "cirrus",
                table: "archive_business_ref",
                columns: new[] { "archive_object_id", "business_reference_type_id", "reference_value", "business_type", "tenant" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_bucket_name_object_key",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "bucket_name", "object_key" },
                unique: true);

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

            migrationBuilder.AddForeignKey(
                name: "fk_archive_business_ref_archive_objects_archive_object_id",
                schema: "cirrus",
                table: "archive_business_ref",
                column: "archive_object_id",
                principalSchema: "cirrus",
                principalTable: "archive_object",
                principalColumn: "archive_object_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
