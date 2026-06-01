namespace EMaigrator.Core.Diagnostics;

/// <summary>A matched, resolved diagnosis returned by <see cref="IErrorCatalog"/> (CONTRACTS.md §3).</summary>
public sealed record ErrorResolution(ErrorRule Rule, string Diagnosis, string Suggestion,
    RemediationKind Kind, RemediationAction RecommendedAction, IReadOnlyList<RemediationAction> Options, Severity Severity);
