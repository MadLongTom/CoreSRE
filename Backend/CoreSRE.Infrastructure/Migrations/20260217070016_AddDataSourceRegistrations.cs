using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSRE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataSourceRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:hstore", ",,");

            migrationBuilder.CreateTable(
                name: "data_source_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    product = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    connection_config = table.Column<string>(type: "jsonb", nullable: false),
                    default_query_config = table.Column<string>(type: "jsonb", nullable: true),
                    health_check = table.Column<string>(type: "jsonb", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_source_registrations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_data_source_registrations_category",
                table: "data_source_registrations",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_data_source_registrations_name",
                table: "data_source_registrations",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_data_source_registrations_product",
                table: "data_source_registrations",
                column: "product");

            migrationBuilder.CreateIndex(
                name: "IX_data_source_registrations_status",
                table: "data_source_registrations",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_source_registrations");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:hstore", ",,");
        }
    }
}
