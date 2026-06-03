using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.Data;

public class EmaigratorDbContext : DbContext
{
    public EmaigratorDbContext(DbContextOptions<EmaigratorDbContext> options) : base(options)
    {
    }

    // Tenant scope for the global query filter. Guid.Empty (the default) disables the filter,
    // so factory-created contexts (Workers/Infra/SecretStore) remain unfiltered. The API sets this
    // per request from the authenticated tenant. See Plan 08 Task 2.
    public Guid CurrentTenantId { get; set; } = Guid.Empty;

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
            // Sentinel tenant filter: Guid.Empty (factory default) leaves reads unfiltered; the API
            // sets CurrentTenantId per request so tenant-scoped reads stay within the caller's tenant.
            e.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
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
            // Sentinel tenant filter (see Job above): Guid.Empty default leaves factory contexts
            // unfiltered; the API scopes reads to the caller's tenant per request.
            e.HasQueryFilter(x => CurrentTenantId == Guid.Empty || x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
        });
    }
}
