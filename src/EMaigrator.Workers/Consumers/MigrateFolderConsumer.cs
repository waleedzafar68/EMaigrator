using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Sessions;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EMaigrator.Workers.Consumers;

/// <summary>
/// Stage 2: page a folder's source messages into bounded MigrateBatch messages (BatchSize from
/// OrchestrationOptions). Stops fanning out if the job is paused or cancelled.
/// </summary>
public sealed partial class MigrateFolderConsumer : IConsumer<MigrateFolder>
{
    private readonly IProviderSessionFactory _sessions;
    private readonly IMessageRefLister _lister;
    private readonly IMigrationControlGate _gate;
    private readonly IMigrationConnectionLookup _lookup;
    private readonly OrchestrationOptions _options;
    private readonly ILogger<MigrateFolderConsumer> _log;

    public MigrateFolderConsumer(
        IProviderSessionFactory sessions,
        IMessageRefLister lister,
        IMigrationControlGate gate,
        IMigrationConnectionLookup lookup,
        IOptions<OrchestrationOptions> options,
        ILogger<MigrateFolderConsumer> log)
    {
        _sessions = sessions;
        _lister = lister;
        _gate = gate;
        _lookup = lookup;
        _options = options.Value;
        _log = log;
    }

    public async Task Consume(ConsumeContext<MigrateFolder> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ct = context.CancellationToken;
        var msg = context.Message;
        var conns = await _lookup.GetAsync(msg.MailboxMigrationId, ct);

        var state = await _gate.GetStateAsync(conns.JobId, ct);
        if (state != MigrationControlState.Active)
        {
            LogHalted(conns.JobId, state);
            return;
        }

        await using var source = await _sessions.CreateSourceAsync(msg.MailboxMigrationId, ct);
        var folder = FolderPath.Parse(msg.SourceFolder);

        var buffer = new List<string>(_options.BatchSize);
        await foreach (var reference in _lister.ListRefsAsync(source, folder, ct))
        {
            buffer.Add(reference);
            if (buffer.Count >= _options.BatchSize)
            {
                await PublishBatchAsync(context, msg, buffer);
                buffer = new List<string>(_options.BatchSize);
            }
        }
        if (buffer.Count > 0)
            await PublishBatchAsync(context, msg, buffer);
    }

    private static Task PublishBatchAsync(ConsumeContext context, MigrateFolder src, List<string> refs)
        => context.Publish(new MigrateBatch(
            src.MailboxMigrationId, src.FolderTaskId, src.SourceFolder, src.DestFolder, refs.ToArray()));

    [LoggerMessage(Level = LogLevel.Information, Message = "MigrateFolder halted — job {JobId} is {State}.")]
    private partial void LogHalted(Guid jobId, MigrationControlState state);
}
