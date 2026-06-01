using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EMaigrator.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretRef = table.Column<string>(type: "text", nullable: false),
                    CipherBlob = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "folder_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MailboxMigrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceFolder = table.Column<string>(type: "text", nullable: false),
                    DestFolder = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folder_tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceProvider = table.Column<string>(type: "text", nullable: false),
                    DestProvider = table.Column<string>(type: "text", nullable: false),
                    SourceConnectionRef = table.Column<string>(type: "text", nullable: true),
                    DestConnectionRef = table.Column<string>(type: "text", nullable: true),
                    IsBatch = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    WizardStep = table.Column<int>(type: "integer", nullable: false),
                    StoreSubjects = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MailboxMigrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityKey = table.Column<string>(type: "text", nullable: false),
                    SourceFolder = table.Column<string>(type: "text", nullable: false),
                    DestFolder = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mailbox_migrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceMailbox = table.Column<string>(type: "text", nullable: false),
                    DestMailbox = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    MigratedCount = table.Column<long>(type: "bigint", nullable: false),
                    SkippedCount = table.Column<long>(type: "bigint", nullable: false),
                    FailedCount = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailbox_migrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "migration_logs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MailboxMigrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    MessageDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceFolder = table.Column<string>(type: "text", nullable: false),
                    DestFolder = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credentials_SecretRef",
                table: "credentials",
                column: "SecretRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credentials_TenantId",
                table: "credentials",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_folder_tasks_MailboxMigrationId",
                table: "folder_tasks",
                column: "MailboxMigrationId");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_TenantId",
                table: "jobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_MailboxMigrationId_IdentityKey",
                table: "ledger_entries",
                columns: new[] { "MailboxMigrationId", "IdentityKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_migrations_JobId",
                table: "mailbox_migrations",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_migration_logs_CreatedAt",
                table: "migration_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_migration_logs_MailboxMigrationId",
                table: "migration_logs",
                column: "MailboxMigrationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credentials");

            migrationBuilder.DropTable(
                name: "folder_tasks");

            migrationBuilder.DropTable(
                name: "jobs");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "mailbox_migrations");

            migrationBuilder.DropTable(
                name: "migration_logs");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
