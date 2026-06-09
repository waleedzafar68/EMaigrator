using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Persistence;
using MassTransit;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Consumers;

public sealed class MigrationCompletionConsumerTests
{
    private static ConsumeContext<MigrationProgressEvent> Ctx(Guid mid)
    {
        var c = Substitute.For<ConsumeContext<MigrationProgressEvent>>();
        c.Message.Returns(new MigrationProgressEvent(mid, 0, 0, "INBOX", 0d, "Running"));
        c.CancellationToken.Returns(CancellationToken.None);
        return c;
    }

    private static IJobStatusFinalizer NotDoneFinalizer()
    {
        var f = Substitute.For<IJobStatusFinalizer>();
        f.FinalizeIfDoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((JobStatus?)null);
        return f;
    }

    [Fact]
    public async Task Writes_terminal_when_pending_zero()
    {
        var mid = Guid.NewGuid();
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(mid, Arg.Any<CancellationToken>()).Returns(new LedgerCounts(5, 0, 0, 0));
        var writer = Substitute.For<IMigrationStatusWriter>();

        await new MigrationCompletionConsumer(ledger, writer, NotDoneFinalizer()).Consume(Ctx(mid));

        await writer.Received(1).SetTerminalAsync(mid,
            Arg.Is<LedgerCounts>(c => c.Pending == 0 && c.Migrated == 5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_nothing_while_pending_remains()
    {
        var mid = Guid.NewGuid();
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(mid, Arg.Any<CancellationToken>()).Returns(new LedgerCounts(3, 0, 0, 7));
        var writer = Substitute.For<IMigrationStatusWriter>();

        await new MigrationCompletionConsumer(ledger, writer, NotDoneFinalizer()).Consume(Ctx(mid));

        await writer.DidNotReceiveWithAnyArgs().SetTerminalAsync(default, default!, default);
    }

    [Fact]
    public async Task Finalizes_and_publishes_terminal_event_when_job_done()
    {
        var mid = Guid.NewGuid();
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(mid, Arg.Any<CancellationToken>()).Returns(new LedgerCounts(5, 0, 0, 0));
        var writer = Substitute.For<IMigrationStatusWriter>();
        var finalizer = Substitute.For<IJobStatusFinalizer>();
        finalizer.FinalizeIfDoneAsync(mid, Arg.Any<CancellationToken>()).Returns(JobStatus.Completed);

        var ctx = Ctx(mid);
        await new MigrationCompletionConsumer(ledger, writer, finalizer).Consume(ctx);

        await writer.Received(1).SetTerminalAsync(mid, Arg.Any<LedgerCounts>(), Arg.Any<CancellationToken>());
        await finalizer.Received(1).FinalizeIfDoneAsync(mid, Arg.Any<CancellationToken>());
        await ctx.Received(1).Publish(
            Arg.Is<MigrationProgressEvent>(e => e.MailboxMigrationId == mid && e.Status == "Completed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_publish_when_job_not_yet_done()
    {
        var mid = Guid.NewGuid();
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(mid, Arg.Any<CancellationToken>()).Returns(new LedgerCounts(5, 0, 0, 0));
        var writer = Substitute.For<IMigrationStatusWriter>();

        var ctx = Ctx(mid);
        await new MigrationCompletionConsumer(ledger, writer, NotDoneFinalizer()).Consume(ctx);

        await ctx.DidNotReceiveWithAnyArgs().Publish(Arg.Any<MigrationProgressEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_reconcile_progress_events()
    {
        var mid = Guid.NewGuid();
        var ledger = Substitute.For<ILedger>();
        var writer = Substitute.For<IMigrationStatusWriter>();
        var finalizer = Substitute.For<IJobStatusFinalizer>();

        var c = Substitute.For<ConsumeContext<MigrationProgressEvent>>();
        c.Message.Returns(new MigrationProgressEvent(mid, 0, 0, "INBOX", 0d, "Running",
            new ReconcileProgress(1, 2, 0, 0, 0)));
        c.CancellationToken.Returns(CancellationToken.None);

        await new MigrationCompletionConsumer(ledger, writer, finalizer).Consume(c);

        // Reconcile drives its own completion in ReconcileConsumer — this consumer must stay out of it.
        await ledger.DidNotReceiveWithAnyArgs().GetCountsAsync(default, default);
        await writer.DidNotReceiveWithAnyArgs().SetTerminalAsync(default, default!, default);
        await finalizer.DidNotReceiveWithAnyArgs().FinalizeIfDoneAsync(default, default);
    }
}
