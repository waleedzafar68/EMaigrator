using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.Retention;

/// <summary>
/// Deletes every stored credential for a job — both the <see cref="CredentialRow"/> records and the
/// backing <see cref="ISecretStore"/> entries — the instant the job reaches a terminal state.
/// No-op while the job is still in a non-terminal state (DESIGN.md §10).
/// </summary>
public sealed class CredentialPurgeHook : ICredentialPurgeHook
{
    private static readonly JobStatus[] Terminal =
    [
        JobStatus.Completed,
        JobStatus.Partial,
        JobStatus.Failed,
        JobStatus.Cancelled,
    ];

    private readonly IDbContextFactory<EmaigratorDbContext> _factory;
    private readonly ISecretStore _secretStore;

    public CredentialPurgeHook(IDbContextFactory<EmaigratorDbContext> factory, ISecretStore secretStore)
    {
        _factory = factory;
        _secretStore = secretStore;
    }

    public async Task PurgeForJobAsync(Guid jobId, CancellationToken ct)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var job = await ctx.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct)
            .ConfigureAwait(false);
        if (job is null || !Terminal.Contains(job.Status))
        {
            return;
        }

        var refs = new[] { job.SourceConnectionRef, job.DestConnectionRef }
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var secretRef in refs)
        {
            await _secretStore.PurgeAsync(secretRef, ct).ConfigureAwait(false);
        }

        await ctx.Credentials
            .Where(c => refs.Contains(c.SecretRef))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
