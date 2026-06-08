using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class ReconcileCommandTests
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
    public async Task Completed_clean_enqueues_reconcile_once_and_returns_success()
    {
        var id = Guid.NewGuid();
        var orch = Substitute.For<IJobOrchestrator>();
        var sw = new StringWriter();

        CliExitCode code = await ReconcileCommand.ExecuteAsync(
            id, orch, StateSequence("Running", "Completed"), LedgerWith(5, 0, 0, 0),
            new HumanOutputWriter(sw), match: null, CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        await orch.Received(1).EnqueueReconcileAsync(id, Arg.Any<CancellationToken>());
        await orch.DidNotReceive().EnqueueMigrationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        sw.ToString().Should().Contain("Completed");
    }

    [Fact]
    public async Task Match_hash_is_accepted()
    {
        var id = Guid.NewGuid();
        var orch = Substitute.For<IJobOrchestrator>();

        CliExitCode code = await ReconcileCommand.ExecuteAsync(
            id, orch, StateSequence("Completed"), LedgerWith(1, 0, 0, 0),
            new HumanOutputWriter(new StringWriter()), match: "hash", CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        await orch.Received(1).EnqueueReconcileAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unsupported_match_value_returns_config_error_without_enqueuing()
    {
        var id = Guid.NewGuid();
        var orch = Substitute.For<IJobOrchestrator>();

        CliExitCode code = await ReconcileCommand.ExecuteAsync(
            id, orch, StateSequence("Completed"), LedgerWith(1, 0, 0, 0),
            new HumanOutputWriter(new StringWriter()), match: "bogus", CancellationToken.None);

        code.Should().Be(CliExitCode.ConfigError);
        await orch.DidNotReceive().EnqueueReconcileAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Completed_with_failures_returns_partial()
    {
        var id = Guid.NewGuid();
        CliExitCode code = await ReconcileCommand.ExecuteAsync(
            id, Substitute.For<IJobOrchestrator>(), StateSequence("Completed"),
            LedgerWith(9, 0, 1, 0), new HumanOutputWriter(new StringWriter()), match: null, CancellationToken.None);

        code.Should().Be(CliExitCode.MigrationPartial);
    }
}
