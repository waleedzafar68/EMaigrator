using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.Data;

public class EmaigratorDbContext : DbContext
{
    public EmaigratorDbContext(DbContextOptions<EmaigratorDbContext> options) : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<MailboxMigration> MailboxMigrations => Set<MailboxMigration>();
    public DbSet<FolderTask> FolderTasks => Set<FolderTask>();
    public DbSet<LedgerEntryRow> LedgerEntries => Set<LedgerEntryRow>();
    public DbSet<MigrationLogRow> MigrationLogs => Set<MigrationLogRow>();
    public DbSet<CredentialRow> Credentials => Set<CredentialRow>();
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var providerIdConverter = new ProviderIdValueConverter();

        modelBuilder.Entity<Job>(e =>
        {
            e.ToTable("jobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceProvider).HasConversion(providerIdConverter).HasColumnType("text");
            e.Property(x => x.DestProvider).HasConversion(providerIdConverter).HasColumnType("text");
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<MailboxMigration>(e =>
        {
            e.ToTable("mailbox_migrations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.JobId);
        });

        modelBuilder.Entity<FolderTask>(e =>
        {
            e.ToTable("folder_tasks");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.MailboxMigrationId);
        });

        modelBuilder.Entity<LedgerEntryRow>(e =>
        {
            e.ToTable("ledger_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => new { x.MailboxMigrationId, x.IdentityKey }).IsUnique();
        });

        modelBuilder.Entity<MigrationLogRow>(e =>
        {
            e.ToTable("migration_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.HasIndex(x => x.MailboxMigrationId);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<CredentialRow>(e =>
        {
            e.ToTable("credentials");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SecretRef).IsUnique();
            e.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
        });
    }
}
