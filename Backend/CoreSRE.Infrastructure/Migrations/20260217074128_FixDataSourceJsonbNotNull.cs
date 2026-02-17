using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSRE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixDataSourceJsonbNotNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing NULL JSONB columns with empty JSON before making NOT NULL
            migrationBuilder.Sql(
                """
                UPDATE data_source_registrations SET default_query_config = '{}' WHERE default_query_config IS NULL;
                UPDATE data_source_registrations SET health_check = '{"IsHealthy":false}' WHERE health_check IS NULL;
                UPDATE data_source_registrations SET metadata = '{}' WHERE metadata IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "metadata",
                table: "data_source_registrations",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "health_check",
                table: "data_source_registrations",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "default_query_config",
                table: "data_source_registrations",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "metadata",
                table: "data_source_registrations",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "health_check",
                table: "data_source_registrations",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "default_query_config",
                table: "data_source_registrations",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");
        }
    }
}
