using EMaigrator.Core.Model;

namespace EMaigrator.Core.Abstractions;

/// <summary>
/// Opaque, validated connection settings: non-secret config plus a secretRef pointing at
/// <see cref="ISecretStore"/>. (CONTRACTS.md §2)
/// </summary>
public sealed record ConnectionDescriptor
{
    public required ProviderId Provider { get; init; }
    public required AuthMethod Auth { get; init; }
    public required IReadOnlyDictionary<string, string> Settings { get; init; }
    public string? SecretRef { get; init; }
}
