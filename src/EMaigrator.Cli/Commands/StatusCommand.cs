using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Commands;

public static class StatusCommand
{
    public static async Task<CliExitCode> ExecuteAsync(
        Guid mailboxMigrationId, IMigrationStateReader stateReader, ILedger ledger,
        IOutputWriter writer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stateReader);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(writer);

        string status = await stateReader.GetStatusAsync(mailboxMigrationId, ct);
        LedgerCounts c = await ledger.GetCountsAsync(mailboxMigrationId, ct);
        writer.WriteStatus(new StatusOutput(
            mailboxMigrationId.ToString(), status, c.Migrated, c.Skipped, c.Failed, c.Pending));
        return CliExitCode.Success;
    }
}
