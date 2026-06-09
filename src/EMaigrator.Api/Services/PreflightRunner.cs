using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Data;
using EMaigrator.Api.Realtime;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Preflight;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Services;

/// <summary>
/// Background pre-flight analysis. Loads the Job + its mailbox rows from the engine
/// <see cref="EmaigratorDbContext"/> (bypassing the tenant query filter — see <see cref="RunAsync"/>),
/// rebuilds the source + destination connectors from the stored descriptors + secrets, invokes the
/// <see cref="IPreflightAnalyzer"/>, persists the serialized plan to the API-owned
/// <see cref="ApiSideContext"/> (the frozen <c>Job</c> has no plan column), flips the Job to
/// <see cref="JobStatus.AwaitingApproval"/>, and pushes the SignalR status change.
/// </summary>
public sealed class PreflightRunner : IPreflightRunner
{
    private readonly EmaigratorDbContext _db;
    private readonly ApiSideContext _side;
    private readonly IPreflightAnalyzer _analyzer;
    private readonly ISecretStore _secrets;
    private readonly IEnumerable<IProviderPlugin> _plugins;
    private readonly IMigrationGroupNotifier _notifier;

    public PreflightRunner(
        EmaigratorDbContext db,
        ApiSideContext side,
        IPreflightAnalyzer analyzer,
        ISecretStore secrets,
        IEnumerable<IProviderPlugin> plugins,
        IMigrationGroupNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(notifier);
        _db = db;
        _side = side;
        _analyzer = analyzer;
        _secrets = secrets;
        _plugins = plugins;
        _notifier = notifier;
    }

    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        // Background scope: the tenant query filter is bypassed here intentionally (no HTTP principal); we
        // already authorized ownership at POST time (via the filtered context), and we load by PK only.
        var job = await _db.Jobs.IgnoreQueryFilters().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
        {
            return;
        }

        var mailboxes = await _db.MailboxMigrations.IgnoreQueryFilters()
            .Where(m => m.JobId == jobId).ToListAsync(ct);

        var srcDescriptor = JsonSerializer.Deserialize<ConnectionDescriptor>(job.SourceConnectionRef!)!;
        var dstDescriptor = JsonSerializer.Deserialize<ConnectionDescriptor>(job.DestConnectionRef!)!;
        var srcPlugin = _plugins.First(p => p.Id.Value == srcDescriptor.Provider.Value);
        var dstPlugin = _plugins.First(p => p.Id.Value == dstDescriptor.Provider.Value);

        var srcSecret = await BundleAsync(srcDescriptor, ct);
        var dstSecret = await BundleAsync(dstDescriptor, ct);

        await using var source = srcPlugin.CreateSource(srcDescriptor, srcSecret);
        await using var dest = dstPlugin.CreateDestination(dstDescriptor, dstSecret);

        var scope = new ScopeSpec
        {
            IsBatch = job.IsBatch,
            Pairs = mailboxes.Select(m => new MailboxPair(m.SourceMailbox, m.DestMailbox)).ToList(),
        };

        var plan = await _analyzer.AnalyzeAsync(source, dest, scope, ct);

        // Persist the plan to the API-owned side table (Job is frozen — no PreflightPlanJson column).
        var planJson = JsonSerializer.Serialize(plan);
        var existing = await _side.PreflightResults.FirstOrDefaultAsync(r => r.JobId == jobId, ct);
        if (existing is null)
        {
            _side.PreflightResults.Add(new PreflightResultRow
            {
                JobId = jobId,
                PlanJson = planJson,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.PlanJson = planJson;
        }

        await _side.SaveChangesAsync(ct);

        job.Status = JobStatus.AwaitingApproval;
        job.WizardStep = Math.Max(job.WizardStep, 4);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _notifier.PushStatusChangedAsync(jobId.ToString(), JobStatus.AwaitingApproval.ToString());
    }

    private async Task<SecretBundle> BundleAsync(ConnectionDescriptor descriptor, CancellationToken ct)
    {
        // Resolve the stored connector-shaped blob exactly as the worker run path does, so preflight
        // analyzes against the real credential under the key the connector reads (CONTRACTS §4).
        if (string.IsNullOrEmpty(descriptor.SecretRef))
        {
            return new SecretBundle(new Dictionary<string, string>(StringComparer.Ordinal));
        }

        return new SecretBundle(SecretBundleShape.Unwrap(await _secrets.RetrieveAsync(descriptor.SecretRef, ct)));
    }
}
