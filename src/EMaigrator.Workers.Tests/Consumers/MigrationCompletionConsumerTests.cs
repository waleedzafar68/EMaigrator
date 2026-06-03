using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
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

    [Fact]
    public async Task Writes_terminal_when_pending_zero()
    {
        var mid = Guid.NewGuid();
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(mid, Arg.Any<CancellationToken>()).Returns(new LedgerCounts(5, 0, 0, 0));
        var writer = Substitute.For<IMigrationStatusWriter>();

        await new MigrationCompletionConsumer(ledger, writer).Consume(Ctx(mid));

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

        await new MigrationCompletionConsumer(ledger, writer).Consume(Ctx(mid));

        await writer.DidNotReceiveWithAnyArgs().SetTerminalAsync(default, default!, default);
    }
}
