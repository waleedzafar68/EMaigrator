using EMaigrator.Core.Model;

namespace EMaigrator.Core.Abstractions;

/// <summary>Token-bucket key per (provider, account) (CONTRACTS.md §4, ARCHITECTURE.md §4).</summary>
public readonly record struct RateLimitKey(ProviderId Provider, string Account);
