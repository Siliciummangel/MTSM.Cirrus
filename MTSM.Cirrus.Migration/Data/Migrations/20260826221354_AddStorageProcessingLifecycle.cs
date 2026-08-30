using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MTSM.Cirrus.Migration.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageProcessingLifecycle : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "storage_processing_attempt_count",
                schema: "cirrus",
                table: "archive_object",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "storage_processing_completed_at",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_processing_error_code",
                schema: "cirrus",
                table: "archive_object",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_processing_error_message",
                schema: "cirrus",
                table: "archive_object",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_processing_lease_owner",
                schema: "cirrus",
                table: "archive_object",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "storage_processing_lease_until",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "storage_processing_next_attempt_at",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "storage_processing_started_at",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_processing_status",
                schema: "cirrus",
                table: "archive_object",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Completed");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "storage_processing_verified_at",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE cirrus.archive_object
                SET storage_processing_status = 'Staged'
                WHERE staging_object_key IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_storage_processing_status_storage_processing",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "storage_processing_status", "storage_processing_next_attempt_at", "storage_processing_lease_until" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_object_storage_processing_attempt_count",
                schema: "cirrus",
                table: "archive_object",
                sql: "storage_processing_attempt_count >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_archive_object_storage_processing_status_storage_processing",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_object_storage_processing_attempt_count",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "storage_processing_attempt_count",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "storage_processing_completed_at",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "storage_processing_error_code",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "storage_processing_error_message",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "storage_processing_lease_owner",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "storage_processing_lease_until",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "storage_processing_next_attempt_at",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "storage_processing_started_at",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "storage_processing_status",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "storage_processing_verified_at",
                schema: "cirrus",
                table: "archive_object");
        }
    }
}
