using System;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Data;

/// <summary>
/// API-owned presentation/orchestration state. NOT in CONTRACTS §5 (keeps the frozen
/// <c>Job</c>/<c>MailboxMigration</c> shapes untouched). Shares the same Npgsql database as the engine,
/// but uses its own <c>__EFMigrationsHistory_ApiSide</c> history table so its migration coexists with the
/// engine's <see cref="EMaigrator.Infrastructure.Data.EmaigratorDbContext"/> (default history) and the
/// Identity context (<c>__EFMigrationsHistory_Identity</c>).
/// </summary>
public sealed class ApiSideContext : DbContext
{
    public ApiSideContext(DbContextOptions<ApiSideContext> options)
        : base(options)
    {
    }

    public DbSet<PreflightResultRow> PreflightResults => Set<PreflightResultRow>();

    public DbSet<ApprovedResolutionRow> ApprovedResolutions => Set<ApprovedResolutionRow>();

    public DbSet<NotificationSentRow> NotificationsSent => Set<NotificationSentRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<PreflightResultRow>().HasKey(r => r.JobId);
        modelBuilder.Entity<ApprovedResolutionRow>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).UseIdentityByDefaultColumn();
        });
        modelBuilder.Entity<NotificationSentRow>().HasKey(r => r.MailboxMigrationId);
    }
}
