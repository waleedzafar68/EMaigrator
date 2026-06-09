using System.Text.Json;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Workers.Sessions;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Workers.Persistence;

/// <summary>
/// Resolves a mailbox-migration id to its parent job's persisted connection descriptors.
/// Connection descriptors live on the Job (Job.SourceConnectionRef / DestConnectionRef), serialized
/// by ConnectionService with default System.Text.Json options — deserialized identically here.
/// </summary>
public sealed class EfMigrationConnectionLookup : IMigrationConnectionLookup
{
    private readonly IDbContextFactory<EmaigratorDbContext> _factory;

    public EfMigrationConnectionLookup(IDbContextFactory<EmaigratorDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<MigrationConnections> GetAsync(Guid mailboxMigrationId, CancellationToken ct)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var migration = await ctx.MailboxMigrations.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mailboxMigrationId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"MailboxMigration {mailboxMigrationId} not found.");

        var job = await ctx.Jobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == migration.JobId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Job {migration.JobId} for migration {mailboxMigrationId} not found.");

        if (string.IsNullOrWhiteSpace(job.SourceConnectionRef) || string.IsNullOrWhiteSpace(job.DestConnectionRef))
        {
            throw new InvalidOperationException($"Job {job.Id} is missing source/dest connection descriptors.");
        }

        var source = JsonSerializer.Deserialize<ConnectionDescriptor>(job.SourceConnectionRef)
            ?? throw new InvalidOperationException($"Job {job.Id} source connection descriptor is invalid JSON.");
        var dest = JsonSerializer.Deserialize<ConnectionDescriptor>(job.DestConnectionRef)
            ?? throw new InvalidOperationException($"Job {job.Id} dest connection descriptor is invalid JSON.");

        return new MigrationConnections(job.Id, job.TenantId.ToString(), source, dest, job.Since, job.Before);
    }
}
