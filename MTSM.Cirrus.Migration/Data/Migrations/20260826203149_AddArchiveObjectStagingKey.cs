using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MTSM.Cirrus.Migration.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveObjectStagingKey : Microsoft.EntityFrameworkCore.Migrations.Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "object_key",
                schema: "cirrus",
                table: "archive_object",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.AddColumn<string>(
                name: "staging_object_key",
                schema: "cirrus",
                table: "archive_object",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_archive_object_tenant_id_bucket_name_staging_object_key",
                schema: "cirrus",
                table: "archive_object",
                columns: new[] { "tenant_id", "bucket_name", "staging_object_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_archive_object_tenant_id_bucket_name_staging_object_key",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.Sql(
                """
                UPDATE cirrus.archive_object
                SET object_key = staging_object_key
                WHERE object_key IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "staging_object_key",
                schema: "cirrus",
                table: "archive_object");

            migrationBuilder.AlterColumn<string>(
                name: "object_key",
                schema: "cirrus",
                table: "archive_object",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);
        }
    }
}
