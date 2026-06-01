using EMaigrator.Core.Model;

namespace EMaigrator.Core.Diagnostics;

/// <summary>
/// A deterministic error-catalog rule: signature regex → diagnosis/suggestion/remediation.
/// The Suggestion MUST NOT echo credentials. (CONTRACTS.md §3)
/// </summary>
public sealed record ErrorRule
{
    public ProviderId? Provider { get; init; }
    public required string SignatureRegex { get; init; }
    public required string Diagnosis { get; init; }
    public required string Suggestion { get; init; }
    public required RemediationKind Kind { get; init; }
    public RemediationAction RecommendedAction { get; init; }
    public IReadOnlyList<RemediationAction> Options { get; init; } = [];
    public required Severity Severity { get; init; }
    public string? HelpUrl { get; init; }
}
