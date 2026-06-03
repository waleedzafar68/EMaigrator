namespace EMaigrator.Cli.Output;

/// <summary>
/// Plain TextWriter-based human output (kept TextWriter-injectable so it is unit-testable;
/// Spectre.Console is used by the live composition root for colored tables/progress).
/// </summary>
public sealed class HumanOutputWriter(TextWriter sink) : IOutputWriter
{
    public void WriteConnectTest(ConnectTestOutput output)
    {
        if (output.Ok)
            sink.WriteLine($"Connection OK — {output.FolderCount} folders, {output.MessageCount} messages.");
        else
            sink.WriteLine($"Connection FAILED — error: {output.ErrorCode ?? "unknown"}.");
    }

    public void WritePreflight(PreflightOutput output)
    {
        sink.WriteLine($"Pre-flight: {output.Estimate.MailboxCount} mailbox(es), " +
                       $"{output.Estimate.FolderCount} folders, {output.Estimate.MessageCount} messages, " +
                       $"{output.Estimate.TotalBytes} bytes.");
        if (output.Issues.Count == 0) { sink.WriteLine("No issues found."); return; }
        sink.WriteLine($"{output.Issues.Count} issue(s):");
        foreach (PreflightIssueOutput i in output.Issues)
            sink.WriteLine($"  [{i.Severity}] {i.IssueType}: {i.Description} " +
                           $"→ recommended: {i.RecommendedAction} (paths: {string.Join(", ", i.AffectedPaths)})");
    }

    public void WriteRun(RunOutput output) =>
        sink.WriteLine($"Run {output.MailboxMigrationId}: status={output.Status} " +
                       $"migrated={output.Migrated} skipped={output.Skipped} failed={output.Failed} pending={output.Pending}.");

    public void WriteStatus(StatusOutput output) =>
        sink.WriteLine($"Migration {output.MailboxMigrationId}: status={output.Status} " +
                       $"migrated={output.Migrated} skipped={output.Skipped} failed={output.Failed} pending={output.Pending}.");

    public void WriteError(string message) => sink.WriteLine($"ERROR: {message}");
}
