namespace EMaigrator.Infrastructure.Secrets;

/// <summary>Managed-KMS key-wrapping seam (Azure Key Vault / AWS KMS). The master key never leaves the KMS.</summary>
public interface IKmsClient
{
    Task<byte[]> WrapKeyAsync(byte[] key, CancellationToken ct);

    Task<byte[]> UnwrapKeyAsync(byte[] wrapped, CancellationToken ct);
}
