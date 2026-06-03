using System;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace EMaigrator.Api.Reporting;

/// <summary>
/// Renders the migration report as CSV via CsvHelper: a key/value summary block (migration id, providers,
/// status, duration), then a per-folder breakdown table with a <c>Folder,Migrated,Skipped,Failed</c> header
/// and a trailing TOTAL row.
/// </summary>
public sealed class CsvReportBuilder : IReportBuilder
{
    private static readonly string[] FolderHeaders = ["Folder", "Migrated", "Skipped", "Failed"];

    public string Format => "csv";

    public string ContentType => "text/csv";

    public string FileName(Guid migrationId) =>
        $"emaigrator-report-{migrationId}.csv";

    public byte[] Build(ReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            InjectionOptions = InjectionOptions.Escape,
        };
        using (var csv = new CsvWriter(sw, config))
        {
            csv.WriteField("Migration");
            csv.WriteField(data.MigrationId.ToString());
            csv.NextRecord();
            csv.WriteField("From");
            csv.WriteField(data.From);
            csv.NextRecord();
            csv.WriteField("To");
            csv.WriteField(data.To);
            csv.NextRecord();
            csv.WriteField("Status");
            csv.WriteField(data.Status);
            csv.NextRecord();
            csv.WriteField("Duration (min)");
            csv.WriteField(Math.Round(data.Duration.TotalMinutes, 1));
            csv.NextRecord();
            csv.NextRecord();

            foreach (var header in FolderHeaders)
            {
                csv.WriteField(header);
            }

            csv.NextRecord();

            foreach (var folder in data.Folders)
            {
                csv.WriteField(folder.Folder);
                csv.WriteField(folder.Migrated);
                csv.WriteField(folder.Skipped);
                csv.WriteField(folder.Failed);
                csv.NextRecord();
            }

            csv.WriteField("TOTAL");
            csv.WriteField(data.Migrated);
            csv.WriteField(data.Skipped);
            csv.WriteField(data.Failed);
            csv.NextRecord();
        }

        return Encoding.UTF8.GetBytes(sw.ToString());
    }
}
