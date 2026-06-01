namespace EMaigrator.Infrastructure.Secrets;

/// <summary>Wraps the per-secret data key through a managed KMS (envelope encryption, hosted mode).</summary>
public sealed class KmsKeyWrapper : IKeyWrapper
{
    private readonly IKmsClient _kms;

    public KmsKeyWrapper(IKmsClient kms)
    {
        ArgumentNullException.ThrowIfNull(kms);
        _kms = kms;
    }

    public Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken ct) => _kms.WrapKeyAsync(dataKey, ct);

    public Task<byte[]> UnwrapAsync(byte[] wrappedDataKey, CancellationToken ct) => _kms.UnwrapKeyAsync(wrappedDataKey, ct);
}
