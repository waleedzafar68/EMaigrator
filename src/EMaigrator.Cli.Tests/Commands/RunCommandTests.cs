using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class RunCommandTests
{
    private static IMigrationStateReader StateSequence(params string[] statuses)
    {
        var reader = Substitute.For<IMigrationStateReader>();
        var queue = new Queue<string>(statuses);
        reader.GetStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(_ => queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        return reader;
    }

    private static ILedger LedgerWith(long migrated, long skipped, long failed, long pending)
    {
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(new LedgerCounts(migrated, skipped, failed, pending));
        return ledger;
    }

    [Fact]
    public async Task Completed_clean_returns_success_and_enqueues_once()
    {
        var id = Guid.NewGuid();
        var orch = Substitute.For<IJobOrchestrator>();
        var sw = new StringWriter();

        CliExitCode code = await RunCommand.ExecuteAsync(
            id, orch, StateSequence("Running", "Completed"), LedgerWith(100, 0, 0, 0),
            new HumanOutputWriter(sw), resume: false, CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        await orch.Received(1).EnqueueMigrationAsync(id, Arg.Any<CancellationToken>());
        sw.ToString().Should().Contain("100").And.Contain("Completed");
    }

    [Fact]
    public async Task Completed_with_failures_returns_partial()
    {
        var id = Guid.NewGuid();
        CliExitCode code = await RunCommand.ExecuteAsync(
            id, Substitute.For<IJobOrchestrator>(), StateSequence("Completed"),
            LedgerWith(90, 0, 10, 0), new HumanOutputWriter(new StringWriter()), false, CancellationToken.None);

        code.Should().Be(CliExitCode.MigrationPartial);
    }

    [Fact]
    public async Task Failed_status_returns_migration_failed()
    {
        var id = Guid.NewGuid();
        CliExitCode code = await RunCommand.ExecuteAsync(
            id, Substitute.For<IJobOrchestrator>(), StateSequence("Failed"),
            LedgerWith(0, 0, 0, 100), new HumanOutputWriter(new StringWriter()), false, CancellationToken.None);

        code.Should().Be(CliExitCode.MigrationFailed);
    }

    [Fact]
    public async Task Resume_enqueues_existing_id_without_creating()
    {
        var id = Guid.NewGuid();
        var orch = Substitute.For<IJobOrchestrator>();

        await RunCommand.ExecuteAsync(
            id, orch, StateSequence("Completed"), LedgerWith(10, 0, 0, 0),
            new HumanOutputWriter(new StringWriter()), resume: true, CancellationToken.None);

        await orch.Received(1).EnqueueMigrationAsync(id, Arg.Any<CancellationToken>());
    }
}
