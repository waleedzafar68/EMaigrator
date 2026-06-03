using System;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Copy;
using EMaigrator.Workers.Sessions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// Stage 3: copy each message in a batch (the idempotent atom). Ledger skips done messages,
/// the rate limiter paces writes; a throttle penalizes the bucket and faults the batch for
/// redelivery. Publishes a live progress event on completion.
/// </summary>
public sealed partial class MigrateBatchConsumer : IConsumer<MigrateBatch>
{
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(10);

    private readonly IProviderSessionFactory _sessions;
    private readonly IMessageHydrator _hydrator;
    private readonly IMigrationControlGate _gate;
    private readonly IMigrationConnectionLookup _lookup;
    private readonly ILedger _ledger;
    private readonly IRateLimiter _limiter;
    private readonly StreamingCopierFactory _copierFactory;
    private readonly ILogger<MigrateBatchConsumer> _log;

    public MigrateBatchConsumer(
        IProviderSessionFactory sessions,
        IMessageHydrator hydrator,
        IMigrationControlGate gate,
        IMigrationConnectionLookup lookup,
        ILedger ledger,
        IRateLimiter limiter,
        StreamingCopierFactory copierFactory,
        ILogger<MigrateBatchConsumer> log)
    {
        _sessions = sessions;
        _hydrator = hydrator;
        _gate = gate;
        _lookup = lookup;
        _ledger = ledger;
        _limiter = limiter;
        _copierFactory = copierFactory;
        _log = log;
    }

    public async Task Consume(ConsumeContext<MigrateBatch> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ct = context.CancellationToken;
        var msg = context.Message;
        var conns = await _lookup.GetAsync(msg.MailboxMigrationId, ct);

        var state = await _gate.GetStateAsync(conns.JobId, ct);
        if (state != MigrationControlState.Active)
        {
            LogDrained(conns.JobId, state);
            return;
        }

        await using var source = await _sessions.CreateSourceAsync(msg.MailboxMigrationId, ct);
        await using var dest = await _sessions.CreateDestinationAsync(msg.MailboxMigrationId, ct);

        var destAccount = conns.Dest.Settings.TryGetValue("accountEmail", out var acct) ? acct : msg.DestFolder;
        var destKey = new RateLimitKey(dest.Id, destAccount);
        var sourceFolder = FolderPath.Parse(msg.SourceFolder);
        var destFolder = FolderPath.Parse(msg.DestFolder);
        var copier = _copierFactory.For(dest);

        foreach (var reference in msg.SourceMessageRefs)
        {
            var message = await _hydrator.HydrateAsync(source, sourceFolder, reference, ct);
            var outcome = await copier.CopyAsync(msg.MailboxMigrationId, destKey, sourceFolder, destFolder, message, ct);
            if (outcome == CopyOutcome.Throttled)
            {
                await _limiter.PenalizeAsync(destKey, DefaultRetryAfter, ct);
                throw new ThrottledRequeueException(
                    $"Throttled on {destKey.Provider.Value}:{destKey.Account}; requeuing batch for redelivery.");
            }
        }

        var counts = await _ledger.GetCountsAsync(msg.MailboxMigrationId, ct);
        var total = counts.Migrated + counts.Skipped + counts.Failed + counts.Pending;
        await context.Publish(new MigrationProgressEvent(
            msg.MailboxMigrationId, counts.Migrated, total, msg.DestFolder, 0d, "Running"));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MigrateBatch drained — job {JobId} is {State}.")]
    private partial void LogDrained(Guid jobId, MigrationControlState state);
}
