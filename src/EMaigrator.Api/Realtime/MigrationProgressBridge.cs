using System;
using System.Linq;
using System.Threading.Tasks;
using EMaigrator.Core.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Api.Realtime;

/// <summary>
/// Consumes worker-published events off the API's MassTransit bus and fans them out over SignalR.
/// <para>
/// Events carry <see cref="MigrationProgressEvent.MailboxMigrationId"/>, but SignalR clients
/// <c>Subscribe</c> to the group keyed by the JOB id (= <c>MigrationDto.id</c>) — a Job fans out to N
/// mailbox rows, so the two ids differ. The bridge therefore resolves the mailbox id to its owning Job
/// id via <see cref="IMailboxJobLookup"/> and pushes to the Job-id group clients actually joined. An
/// unknown mailbox is a defensive no-op (logged warning).
/// </para>
/// <para>
/// Hub pushes are best-effort: a transient SignalR/backplane failure must not fault the MassTransit
/// pipeline (which would trigger redelivery), so push exceptions are logged and swallowed.
/// </para>
/// </summary>
public sealed partial class MigrationProgressBridge :
    IConsumer<MigrationProgressEvent>, IConsumer<NeedsDecisionEvent>
{
    private readonly IMigrationGroupNotifier _notifier;
    private readonly IMailboxJobLookup _lookup;
    private readonly ILogger<MigrationProgressBridge> _logger;

    public MigrationProgressBridge(
        IMigrationGroupNotifier notifier,
        IMailboxJobLookup lookup,
        ILogger<MigrationProgressBridge> logger)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(logger);
        _notifier = notifier;
        _lookup = lookup;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<MigrationProgressEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var m = context.Message;

        var jobId = await _lookup.GetJobIdAsync(m.MailboxMigrationId, context.CancellationToken)
            .ConfigureAwait(false);
        if (jobId is null)
        {
            LogUnknownMailbox(m.MailboxMigrationId);
            return;
        }

        var migrationId = jobId.Value.ToString();
        try
        {
            await Task.WhenAll(
                _notifier.PushProgressAsync(
                    new MigrationProgressDto(migrationId, m.Migrated, m.Total, m.CurrentFolder, m.MsgPerMin, m.Status)),
                _notifier.PushStatusChangedAsync(migrationId, m.Status))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogPushFailed(ex, migrationId);
        }
    }

    public async Task Consume(ConsumeContext<NeedsDecisionEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var m = context.Message;

        var jobId = await _lookup.GetJobIdAsync(m.MailboxMigrationId, context.CancellationToken)
            .ConfigureAwait(false);
        if (jobId is null)
        {
            LogUnknownMailbox(m.MailboxMigrationId);
            return;
        }

        var migrationId = jobId.Value.ToString();
        try
        {
            await _notifier.PushNeedsDecisionAsync(migrationId,
                new NeedsDecisionDto(m.IssueType, m.Detail, m.Options.Select(o => o.ToString()).ToArray()))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogPushFailed(ex, migrationId);
        }
    }

    private void LogUnknownMailbox(Guid mailboxMigrationId) => UnknownMailbox(_logger, mailboxMigrationId);

    private void LogPushFailed(Exception ex, string migrationId) => PushFailed(_logger, migrationId, ex);

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "MigrationProgressBridge received an event for unknown MailboxMigrationId {MailboxMigrationId}; dropping.")]
    private static partial void UnknownMailbox(ILogger logger, Guid mailboxMigrationId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "MigrationProgressBridge failed to push a SignalR event for migration {MigrationId}; continuing (best-effort).")]
    private static partial void PushFailed(ILogger logger, string migrationId, Exception ex);
}
