using System;
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace EMaigrator.Api.Reporting;

/// <summary>
/// Renders the migration report as a PDF via QuestPDF: a header, the summary line (status/duration/counts),
/// and a per-folder breakdown table. The static constructor sets the QuestPDF Community license (free for
/// OSS use), which must be assigned before any document is generated.
/// </summary>
public sealed class PdfReportBuilder : IReportBuilder
{
    private static readonly string[] FolderHeaders = ["Folder", "Migrated", "Skipped", "Failed"];

    static PdfReportBuilder() => QuestPDF.Settings.License = LicenseType.Community;

    public string Format => "pdf";

    public string ContentType => "application/pdf";

    public string FileName(Guid migrationId) =>
        $"emaigrator-report-{migrationId}.pdf";

    public byte[] Build(ReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var durationMin = Math.Round(data.Duration.TotalMinutes, 1)
            .ToString(CultureInfo.InvariantCulture);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(40);
                page.Header()
                    .Text($"EMaigrator Report — {data.From} → {data.To}")
                    .Bold().FontSize(16);
                page.Content().Column(col =>
                {
                    col.Item().Text($"Status: {data.Status}   Duration: {durationMin} min");
                    col.Item().Text(
                        $"Migrated: {data.Migrated}   Skipped: {data.Skipped}   Failed: {data.Failed}");
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        foreach (var header in FolderHeaders)
                        {
                            table.Cell().Text(header).Bold();
                        }

                        foreach (var folder in data.Folders)
                        {
                            table.Cell().Text(folder.Folder);
                            table.Cell().Text(folder.Migrated.ToString(CultureInfo.InvariantCulture));
                            table.Cell().Text(folder.Skipped.ToString(CultureInfo.InvariantCulture));
                            table.Cell().Text(folder.Failed.ToString(CultureInfo.InvariantCulture));
                        }
                    });
                });
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Migration ");
                    text.Span(data.MigrationId.ToString());
                });
            });
        }).GeneratePdf();
    }
}
