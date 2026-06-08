using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EMaigrator.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "jobs",
                type: "text",
                nullable: false,
                defaultValue: "Migrate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "jobs");
        }
    }
}
