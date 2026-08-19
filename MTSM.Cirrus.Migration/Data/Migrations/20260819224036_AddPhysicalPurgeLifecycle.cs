using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MTSM.Cirrus.Migration.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhysicalPurgeLifecycle : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_archive_object_archive_status_deletion_requested_at",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.AddColumn<string>(
                name: "purge_lease_owner",
                schema: "cirrus",
                table: "archive_object",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "purge_lease_until",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_archive_status_retention_until_purge_lease_u",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "archive_status", "retention_until", "purge_lease_until" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_archive_object_archive_status_retention_until_purge_lease_u",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "purge_lease_owner",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "purge_lease_until",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_archive_status_deletion_requested_at",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "archive_status", "deletion_requested_at" });
        }
    }
}
