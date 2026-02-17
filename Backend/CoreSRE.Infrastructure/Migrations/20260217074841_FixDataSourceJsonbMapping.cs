using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSRE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixDataSourceJsonbMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:hstore", ",,");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:hstore", ",,");
        }
    }
}
