using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Commands;

public static class RunCommand
{
    private static readonly string[] TerminalStatuses =
        ["Completed", "Partial", "Failed", "Cancelled"];

    public static async Task<CliExitCode> ExecuteAsync(
        Guid mailboxMigrationId, IJobOrchestrator orchestrator, IMigrationStateReader stateReader,
        ILedger ledger, IOutputWriter writer, bool resume, CancellationToken ct,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(stateReader);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(writer);

        TimeSpan interval = pollInterval ?? TimeSpan.FromSeconds(2);

        try
        {
            // Resume and fresh run are identical at the orchestration seam: enqueue → workers
            // scan the ledger and (re-)process not-done items. (ARCHITECTURE.md §6)
            await orchestrator.EnqueueMigrationAsync(mailboxMigrationId, ct);

            string status;
            do
            {
                status = await stateReader.GetStatusAsync(mailboxMigrationId, ct);
                if (Array.IndexOf(TerminalStatuses, status) >= 0) break;
                await Task.Delay(interval, ct);
            } while (true);

            LedgerCounts counts = await ledger.GetCountsAsync(mailboxMigrationId, ct);
            writer.WriteRun(new RunOutput(
                mailboxMigrationId.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
                counts.Migrated, counts.Skipped, counts.Failed, counts.Pending, status));

            return MapExit(status, counts);
        }
        catch (OperationCanceledException)
        {
            writer.WriteError("Run cancelled.");
            return CliExitCode.Cancelled;
        }
    }

    private static CliExitCode MapExit(string status, LedgerCounts counts) => status switch
    {
        "Completed" when counts.Failed == 0 => CliExitCode.Success,
        "Completed" => CliExitCode.MigrationPartial,
        "Partial" => CliExitCode.MigrationPartial,
        _ => CliExitCode.MigrationFailed, // Failed | Cancelled
    };
}
