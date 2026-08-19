using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MTSM.Cirrus.Migration.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantToArchiveEvents : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_archive_event_archive_objects_archive_object_id",
                schema: "cirrus",
                table: "archive_event");

            migrationBuilder.DropIndex(
                name: "ix_archive_event_archive_object_id_event_timestamp",
                schema: "cirrus",
                table: "archive_event");

            migrationBuilder.DropIndex(
                name: "ix_archive_event_event_type",
                schema: "cirrus",
                table: "archive_event");

            migrationBuilder.AddColumn<long>(
                name: "tenant_id",
                schema: "cirrus",
                table: "archive_event",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE cirrus.archive_event AS archive_event
                SET tenant_id = archive_object.tenant_id
                FROM cirrus.archive_object AS archive_object
                WHERE archive_object.archive_object_id = archive_event.archive_object_id;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "tenant_id",
                schema: "cirrus",
                table: "archive_event",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_archive_event_tenant_id_archive_object_id_event_timestamp",
                schema: "cirrus",
                table: "archive_event",
                columns: new[] { "tenant_id", "archive_object_id", "event_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_event_tenant_id_event_timestamp",
                schema: "cirrus",
                table: "archive_event",
                columns: new[] { "tenant_id", "event_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_event_tenant_id_event_type_event_timestamp",
                schema: "cirrus",
                table: "archive_event",
                columns: new[] { "tenant_id", "event_type", "event_timestamp" });

            migrationBuilder.AddForeignKey(
                name: "fk_archive_event_archive_objects_tenant_id_archive_object_id",
                schema: "cirrus",
                table: "archive_event",
                columns: new[] { "tenant_id", "archive_object_id" },
                principalSchema: "cirrus",
                principalTable: "archive_object",
                principalColumns: new[] { "tenant_id", "archive_object_id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_archive_event_archive_objects_tenant_id_archive_object_id",
                schema: "cirrus",
                table: "archive_event");

            migrationBuilder.DropIndex(
                name: "ix_archive_event_tenant_id_archive_object_id_event_timestamp",
                schema: "cirrus",
                table: "archive_event");

            migrationBuilder.DropIndex(
                name: "ix_archive_event_tenant_id_event_timestamp",
                schema: "cirrus",
                table: "archive_event");

            migrationBuilder.DropIndex(
                name: "ix_archive_event_tenant_id_event_type_event_timestamp",
                schema: "cirrus",
                table: "archive_event");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "cirrus",
                table: "archive_event");

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

            migrationBuilder.AddForeignKey(
                name: "fk_archive_event_archive_objects_archive_object_id",
                schema: "cirrus",
                table: "archive_event",
                column: "archive_object_id",
                principalSchema: "cirrus",
                principalTable: "archive_object",
                principalColumn: "archive_object_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
