using System.Globalization;
using System.Text;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Cli.Commands;

public static class ReportCommand
{
    public static async Task<CliExitCode> ExecuteAsync(
        Guid mailboxMigrationId, ILedger ledger, TextWriter csvOut, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(csvOut);

        await csvOut.WriteLineAsync("identityKey,sourceFolder,destFolder,status,errorCode,updatedAt");
        await foreach (LedgerEntry e in ledger.GetNotDoneAsync(mailboxMigrationId, ct))
        {
            var row = new StringBuilder()
                .Append(Csv(e.IdentityKey)).Append(',')
                .Append(Csv(e.SourceFolder)).Append(',')
                .Append(Csv(e.DestFolder)).Append(',')
                .Append(Csv(e.Status.ToString())).Append(',')
                .Append(Csv(e.ErrorCode ?? "")).Append(',')
                .Append(Csv(e.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)));
            await csvOut.WriteLineAsync(row.ToString());
        }
        return CliExitCode.Success;
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return value;
    }
}
