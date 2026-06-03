using System;
using System.Threading.Tasks;
using EMaigrator.Api.Realtime;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Diagnostics;
using FluentAssertions;
using MassTransit;
using NSubstitute;

namespace EMaigrator.Api.Tests;

/// <summary>
/// Pure unit tests for the <see cref="MigrationProgressBridge"/> MassTransit consumer: a
/// <see cref="MigrationProgressEvent"/> maps to a group <c>PushProgressAsync</c> and a
/// <see cref="NeedsDecisionEvent"/> maps to a group <c>PushNeedsDecisionAsync</c>. No broker or
/// fixture is involved — the consume context and the notifier are NSubstitute fakes.
/// </summary>
public sealed class MigrationProgressBridgeTests
{
    [Fact]
    public async Task Consuming_progress_event_pushes_to_group()
    {
        var notifier = Substitute.For<IMigrationGroupNotifier>();
        var bridge = new MigrationProgressBridge(notifier);
        var mbxId = Guid.NewGuid();

        var ctx = Substitute.For<ConsumeContext<MigrationProgressEvent>>();
        ctx.Message.Returns(new MigrationProgressEvent(mbxId, 7, 10, "/Sent", 99.0, "Running"));
        await bridge.Consume(ctx);

        await notifier.Received(1).PushProgressAsync(Arg.Is<MigrationProgressDto>(
            d => d.Migrated == 7 && d.Total == 10 && d.Status == "Running"));
    }

    [Fact]
    public async Task Consuming_needs_decision_event_pushes_to_group()
    {
        var notifier = Substitute.For<IMigrationGroupNotifier>();
        var bridge = new MigrationProgressBridge(notifier);

        var ctx = Substitute.For<ConsumeContext<NeedsDecisionEvent>>();
        ctx.Message.Returns(new NeedsDecisionEvent(Guid.NewGuid(), "FolderCollision", "name clash",
            new[] { RemediationAction.RenameFolder }));
        await bridge.Consume(ctx);

        await notifier.Received(1).PushNeedsDecisionAsync(Arg.Any<string>(), Arg.Any<NeedsDecisionDto>());
    }
}
