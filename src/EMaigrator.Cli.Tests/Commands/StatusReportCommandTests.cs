using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class StatusReportCommandTests
{
    private static ILedger Ledger(LedgerCounts counts, params LedgerEntry[] entries)
    {
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(counts);
        ledger.GetNotDoneAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(_ => ToAsync(entries));
        return ledger;
    }

    private static async IAsyncEnumerable<LedgerEntry> ToAsync(LedgerEntry[] entries)
    {
        foreach (var e in entries) { yield return e; }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Status_writes_counts_and_returns_success()
    {
        var id = Guid.NewGuid();
        var reader = Substitute.For<IMigrationStateReader>();
        reader.GetStatusAsync(id, Arg.Any<CancellationToken>()).Returns("Running");
        var sw = new StringWriter();

        CliExitCode code = await StatusCommand.ExecuteAsync(
            id, reader, Ledger(new LedgerCounts(50, 2, 1, 47)), new HumanOutputWriter(sw), CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        sw.ToString().Should().Contain("Running").And.Contain("50");
    }

    [Fact]
    public async Task Report_csv_has_metadata_only_header_and_no_content_columns()
    {
        var id = Guid.NewGuid();
        var entry = new LedgerEntry(id, "mid:<abc@x>", "Inbox", "Inbox",
            LedgerStatus.Failed, "WRITE_FAILED", DateTimeOffset.UnixEpoch);
        var sw = new StringWriter();

        CliExitCode code = await ReportCommand.ExecuteAsync(
            id, Ledger(new LedgerCounts(0, 0, 1, 0), entry), sw, CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        string csv = sw.ToString();
        csv.Should().StartWith("identityKey,sourceFolder,destFolder,status,errorCode,updatedAt");
        csv.Should().Contain("mid:<abc@x>").And.Contain("WRITE_FAILED");
        csv.ToLowerInvariant().Should().NotContain("body").And.NotContain("subject")
            .And.NotContain("sender").And.NotContain("recipient");
    }

    [Fact]
    public async Task Report_csv_escapes_commas_and_quotes()
    {
        var id = Guid.NewGuid();
        var entry = new LedgerEntry(id, "mid:<a,b>", "A \"B\"", "C", LedgerStatus.Migrated, null, DateTimeOffset.UnixEpoch);
        var sw = new StringWriter();

        await ReportCommand.ExecuteAsync(id, Ledger(new LedgerCounts(1, 0, 0, 0), entry), sw, CancellationToken.None);

        sw.ToString().Should().Contain("\"mid:<a,b>\"").And.Contain("\"A \"\"B\"\"\"");
    }
}
