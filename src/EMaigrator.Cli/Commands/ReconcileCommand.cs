using EMaigrator.Cli.Output;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Commands;

/// <summary>
/// Reconcile an EXISTING migration against the live destination: enqueue a reconcile run (publishes
/// ReconcileMailbox) and poll to a terminal status, then print the ledger counts. Mirrors
/// <see cref="RunCommand"/> but drives the reconcile seam. Secrets resolve via the connector-shaped
/// secret bundle inside the in-process worker (no plaintext on the command line).
/// </summary>
public static class ReconcileCommand
{
    private static readonly string[] TerminalStatuses =
        ["Completed", "Partial", "Failed", "Cancelled"];

    public static async Task<CliExitCode> ExecuteAsync(
        Guid mailboxMigrationId, IJobOrchestrator orchestrator, IMigrationStateReader stateReader,
        ILedger ledger, IOutputWriter writer, string? match, CancellationToken ct,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(stateReader);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(writer);

        // --match is accepted + recorded now; "metadata" (default) diffs attachments on Name+ContentType.
        // Strict "hash" matching at the destination is a deferred follow-up (design §9): the flag stays
        // forward-compatible, but reconcile still matches on metadata for v1.
        if (!string.IsNullOrWhiteSpace(match)
            && !string.Equals(match, "metadata", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(match, "hash", StringComparison.OrdinalIgnoreCase))
        {
            writer.WriteError($"Unsupported --match value '{match}'. Use 'metadata' (default) or 'hash'.");
            return CliExitCode.ConfigError;
        }

        TimeSpan interval = pollInterval ?? TimeSpan.FromSeconds(2);

        try
        {
            await orchestrator.EnqueueReconcileAsync(mailboxMigrationId, ct);

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
            writer.WriteError("Reconcile cancelled.");
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
