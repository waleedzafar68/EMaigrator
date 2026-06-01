using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using EMaigrator.Core.Configuration;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.Secrets;

/// <summary>
/// Azure Key Vault KMS client. KeyRef is the full key identifier
/// (e.g. https://&lt;vault&gt;.vault.azure.net/keys/&lt;name&gt;). Wrap/unwrap run inside Key Vault
/// via RSA-OAEP; the master key never leaves the vault.
/// </summary>
public sealed class AzureKeyVaultKmsClient : IKmsClient
{
    private readonly CryptographyClient _crypto;

    public AzureKeyVaultKmsClient(IOptions<SecretStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var keyId = options.Value.KeyRef
            ?? throw new InvalidOperationException("SecretStore:KeyRef (Key Vault key id) is required for AzureKeyVault mode.");
        _crypto = new CryptographyClient(new Uri(keyId), new DefaultAzureCredential());
    }

    public async Task<byte[]> WrapKeyAsync(byte[] key, CancellationToken ct)
    {
        var result = await _crypto.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, key, ct).ConfigureAwait(false);
        return result.EncryptedKey;
    }

    public async Task<byte[]> UnwrapKeyAsync(byte[] wrapped, CancellationToken ct)
    {
        var result = await _crypto.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrapped, ct).ConfigureAwait(false);
        return result.Key;
    }
}
