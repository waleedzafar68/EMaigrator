using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EMaigrator.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApiSideNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationsSent",
                columns: table => new
                {
                    MailboxMigrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationsSent", x => x.MailboxMigrationId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationsSent");
        }
    }
}
