using EMaigrator.Core.Model;

namespace EMaigrator.Core.Diagnostics;

/// <summary>Optional AI fallback for the unknown tail; never auto-fixes (CONTRACTS.md §3).</summary>
public interface IErrorExplainer
{
    Task<ErrorResolution?> ExplainAsync(ProviderId provider, string errorSignature, CancellationToken ct);
}
