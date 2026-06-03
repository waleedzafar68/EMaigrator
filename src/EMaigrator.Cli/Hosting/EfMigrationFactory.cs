using System.Text.Json;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Preflight;   // MailboxPair
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Cli.Hosting;

/// <summary>
/// Live IMigrationFactory: resolves a secret per side (env/prompt -> ISecretStore via SecretResolver,
/// stored as connector-shaped JSON), persists a Job (with JsonSerializer.Serialize(ConnectionDescriptor)
/// source/dest refs whose SecretRef points at the stored blob) plus one MailboxMigration{Pending} per
/// scope pair, and returns the first migration id. The worker (StartMigrationConsumer) seeds the Pending
/// ledger and performs the copy — this factory does NOT touch the ledger.
/// </summary>
public sealed class EfMigrationFactory(
    IDbContextFactory<EmaigratorDbContext> dbFactory,
    SecretResolver secretResolver) : IMigrationFactory
{
    public async Task<Guid> CreateAsync(MigrationProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // One tenant Guid for both the Job and the secret-store tenant (keep them consistent).
        var tenant = Guid.NewGuid();

        string fromRef = await secretResolver.ResolveAsync(MigrationSide.From, profile.From, tenant.ToString(), ct);
        string toRef = await secretResolver.ResolveAsync(MigrationSide.To, profile.To, tenant.ToString(), ct);

        ConnectionDescriptor src = ConnectionBuilder.BuildDescriptor(profile.From, fromRef);
        ConnectionDescriptor dest = ConnectionBuilder.BuildDescriptor(profile.To, toRef);

        var jobId = Guid.NewGuid();
        var firstMigrationId = Guid.Empty;

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        ctx.Jobs.Add(new Job
        {
            Id = jobId,
            TenantId = tenant,
            SourceProvider = profile.From.Provider,
            DestProvider = profile.To.Provider,
            SourceConnectionRef = JsonSerializer.Serialize(src),
            DestConnectionRef = JsonSerializer.Serialize(dest),
            IsBatch = profile.Scope.IsBatch,
            StoreSubjects = profile.StoreSubjects,
            Status = JobStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        foreach (MailboxPair pair in profile.Scope.Pairs)
        {
            var migrationId = Guid.NewGuid();
            if (firstMigrationId == Guid.Empty) firstMigrationId = migrationId;
            ctx.MailboxMigrations.Add(new MailboxMigration
            {
                Id = migrationId,
                JobId = jobId,
                SourceMailbox = pair.SourceMailbox,
                DestMailbox = pair.DestMailbox,
                Status = MailboxMigrationStatus.Pending,
            });
        }

        await ctx.SaveChangesAsync(ct);
        return firstMigrationId;
    }
}
