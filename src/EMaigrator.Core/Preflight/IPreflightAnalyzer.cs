using EMaigrator.Core.Abstractions;

namespace EMaigrator.Core.Preflight;

/// <summary>Read-only scan of source tree against destination constraints → a remediation plan (CONTRACTS.md §3).</summary>
public interface IPreflightAnalyzer
{
    Task<PreflightPlan> AnalyzeAsync(ISourceProvider source, IDestinationProvider dest, ScopeSpec scope, CancellationToken ct);
}
