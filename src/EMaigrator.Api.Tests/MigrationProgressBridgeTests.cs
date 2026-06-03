using System;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Realtime;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Diagnostics;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Pure unit tests for the <see cref="MigrationProgressBridge"/> MassTransit consumer: it resolves the
/// event's <c>MailboxMigrationId</c> to the owning Job id via <see cref="IMailboxJobLookup"/> and pushes
/// to the JOB-id SignalR group (the key clients <c>Subscribe</c> to). A
/// <see cref="MigrationProgressEvent"/> maps to <c>PushProgressAsync</c> and a
/// <see cref="NeedsDecisionEvent"/> maps to <c>PushNeedsDecisionAsync</c>. No broker or fixture is
/// involved — the consume context, lookup, and notifier are NSubstitute fakes.
/// </summary>
public sealed class MigrationProgressBridgeTests
{
    [Fact]
    public async Task Consuming_progress_event_resolves_job_and_pushes_to_job_group()
    {
        var notifier = Substitute.For<IMigrationGroupNotifier>();
        var lookup = Substitute.For<IMailboxJobLookup>();
        var mbxId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        lookup.GetJobIdAsync(mbxId, Arg.Any<CancellationToken>()).Returns(jobId);

        var bridge = new MigrationProgressBridge(notifier, lookup, NullLogger<MigrationProgressBridge>.Instance);

        var ctx = Substitute.For<ConsumeContext<MigrationProgressEvent>>();
        ctx.Message.Returns(new MigrationProgressEvent(mbxId, 7, 10, "/Sent", 99.0, "Running"));
        await bridge.Consume(ctx);

        // Pushed to the JOB-id group (not the mailbox id) — this guards the group-key correctness.
        await notifier.Received(1).PushProgressAsync(Arg.Is<MigrationProgressDto>(
            d => d.MigrationId == jobId.ToString() && d.Migrated == 7 && d.Total == 10 && d.Status == "Running"));
        await notifier.Received(1).PushStatusChangedAsync(jobId.ToString(), "Running");
    }

    [Fact]
    public async Task Consuming_needs_decision_event_resolves_job_and_pushes_to_job_group()
    {
        var notifier = Substitute.For<IMigrationGroupNotifier>();
        var lookup = Substitute.For<IMailboxJobLookup>();
        var mbxId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        lookup.GetJobIdAsync(mbxId, Arg.Any<CancellationToken>()).Returns(jobId);

        var bridge = new MigrationProgressBridge(notifier, lookup, NullLogger<MigrationProgressBridge>.Instance);

        var ctx = Substitute.For<ConsumeContext<NeedsDecisionEvent>>();
        ctx.Message.Returns(new NeedsDecisionEvent(mbxId, "FolderCollision", "name clash",
            new[] { RemediationAction.RenameFolder }));
        await bridge.Consume(ctx);

        await notifier.Received(1).PushNeedsDecisionAsync(jobId.ToString(), Arg.Any<NeedsDecisionDto>());
    }

    [Fact]
    public async Task Unknown_mailbox_is_a_no_op()
    {
        var notifier = Substitute.For<IMigrationGroupNotifier>();
        var lookup = Substitute.For<IMailboxJobLookup>();
        lookup.GetJobIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var bridge = new MigrationProgressBridge(notifier, lookup, NullLogger<MigrationProgressBridge>.Instance);

        var ctx = Substitute.For<ConsumeContext<MigrationProgressEvent>>();
        ctx.Message.Returns(new MigrationProgressEvent(Guid.NewGuid(), 1, 2, null, 0, "Running"));
        await bridge.Consume(ctx);

        await notifier.DidNotReceive().PushProgressAsync(Arg.Any<MigrationProgressDto>());
        await notifier.DidNotReceive().PushStatusChangedAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
