namespace EMaigrator.Infrastructure.Secrets;

/// <summary>
/// Wraps/unwraps a per-secret data key with a master key. The local implementation wraps in-process;
/// the KMS implementation delegates to Azure Key Vault / AWS KMS.
/// </summary>
public interface IKeyWrapper
{
    Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken ct);

    Task<byte[]> UnwrapAsync(byte[] wrappedDataKey, CancellationToken ct);
}
