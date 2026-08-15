using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MTSM.Cirrus.Migration.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledIntegrityChecks : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "integrity_check_lease_owner",
                schema: "cirrus",
                table: "archive_object",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "integrity_check_lease_until",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_integrity_check_at",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_integrity_check_at",
                schema: "cirrus",
                table: "archive_object",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_archive_status_next_integrity_check_at_integ",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "archive_status", "next_integrity_check_at", "integrity_check_lease_until" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_archive_object_archive_status_next_integrity_check_at_integ",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "integrity_check_lease_owner",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "integrity_check_lease_until",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "last_integrity_check_at",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.DropColumn(
                name: "next_integrity_check_at",
                schema: "cirrus",
                table: "archive_object");
        }
    }
}
