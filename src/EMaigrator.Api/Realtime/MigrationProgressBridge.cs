using System;
using System.Linq;
using System.Threading.Tasks;
using EMaigrator.Core.Contracts;
using MassTransit;

namespace EMaigrator.Api.Realtime;

/// <summary>
/// Consumes worker-published events off the API's MassTransit bus and fans them out over SignalR. The
/// SignalR group key is the migration id; <c>MailboxMigrationId</c> maps 1:1 to the migration's mailbox
/// unit, so the group is its string form.
/// </summary>
public sealed class MigrationProgressBridge :
    IConsumer<MigrationProgressEvent>, IConsumer<NeedsDecisionEvent>
{
    private readonly IMigrationGroupNotifier _notifier;

    public MigrationProgressBridge(IMigrationGroupNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        _notifier = notifier;
    }

    public Task Consume(ConsumeContext<MigrationProgressEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var m = context.Message;
        var migrationId = m.MailboxMigrationId.ToString();
        return Task.WhenAll(
            _notifier.PushProgressAsync(
                new MigrationProgressDto(migrationId, m.Migrated, m.Total, m.CurrentFolder, m.MsgPerMin, m.Status)),
            _notifier.PushStatusChangedAsync(migrationId, m.Status));
    }

    public Task Consume(ConsumeContext<NeedsDecisionEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var m = context.Message;
        var migrationId = m.MailboxMigrationId.ToString();
        return _notifier.PushNeedsDecisionAsync(migrationId,
            new NeedsDecisionDto(m.IssueType, m.Detail, m.Options.Select(o => o.ToString()).ToArray()));
    }
}
