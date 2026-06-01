namespace EMaigrator.Core.Abstractions;

/// <summary>Credential storage seam (KMS envelope vs local key). Transient plaintext only (CONTRACTS.md §4).</summary>
public interface ISecretStore
{
    Task<string> StoreAsync(string tenantId, string plaintext, CancellationToken ct);
    Task<string> RetrieveAsync(string secretRef, CancellationToken ct);
    Task PurgeAsync(string secretRef, CancellationToken ct);
}
