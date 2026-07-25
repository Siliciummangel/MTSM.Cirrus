using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MTSM.Cirrus.Migration.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveDeletionLifecycle : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deletion_requested_at",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deletion_requested_by",
                schema: "cirrus",
                table: "archive_object",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "purged_at",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_archive_status_deletion_requested_at",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "archive_status", "deletion_requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_deletion_requested_at",
                schema: "cirrus",
                table: "archive_object",
                column: "deletion_requested_at");

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_purged_at",
                schema: "cirrus",
                table: "archive_object",
                column: "purged_at");

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_object_deletion_requested",
                schema: "cirrus",
                table: "archive_object",
                sql: "archive_status <> 'DeletionRequested'\r\nOR (\r\n    deletion_requested_at IS NOT NULL\r\n    AND deletion_requested_by IS NOT NULL\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_archive_object_purged",
                schema: "cirrus",
                table: "archive_object",
                sql: "archive_status <> 'Purged'\r\nOR purged_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_archive_object_archive_status_deletion_requested_at",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropIndex(
                name: "ix_archive_object_deletion_requested_at",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropIndex(
                name: "ix_archive_object_purged_at",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_object_deletion_requested",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropCheckConstraint(
                name: "ck_archive_object_purged",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "deletion_requested_at",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "deletion_requested_by",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "purged_at",
                schema: "cirrus",
                table: "archive_object");
        }
    }
}
