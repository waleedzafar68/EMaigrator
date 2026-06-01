using EMaigrator.Core.Model;

namespace EMaigrator.Core.Diagnostics;

/// <summary>Deterministic rule catalog matching normalized error signatures (CONTRACTS.md §3).</summary>
public interface IErrorCatalog
{
    ErrorResolution? Match(ProviderId provider, string errorSignature);
}
