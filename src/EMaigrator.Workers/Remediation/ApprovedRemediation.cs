using EMaigrator.Core.Diagnostics;

namespace EMaigrator.Workers.Remediation;

/// <summary>An operator-approved structural remediation for a single source folder.</summary>
public sealed record ApprovedRemediation(string SourceFolder, RemediationAction Action);
