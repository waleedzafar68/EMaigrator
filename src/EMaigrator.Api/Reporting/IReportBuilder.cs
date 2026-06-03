using System;

namespace EMaigrator.Api.Reporting;

/// <summary>
/// A pluggable report renderer. The endpoint resolves the <see cref="IEnumerable{IReportBuilder}"/> and
/// selects by <see cref="Format"/> (the lower-cased <c>?format=</c> query value).
/// </summary>
public interface IReportBuilder
{
    /// <summary>The format token this builder handles, e.g. <c>"csv"</c> or <c>"pdf"</c>.</summary>
    string Format { get; }

    /// <summary>The response <c>Content-Type</c> for the rendered bytes.</summary>
    string ContentType { get; }

    /// <summary>The download file name for the given migration id.</summary>
    string FileName(Guid migrationId);

    /// <summary>Renders the report to its on-the-wire byte representation.</summary>
    byte[] Build(ReportData data);
}
