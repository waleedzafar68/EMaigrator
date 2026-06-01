using System.Security.Cryptography;
using EMaigrator.Core.Configuration;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.Secrets;

/// <summary>Wraps the data key with a config-provided 32-byte AES master key (self-host mode).</summary>
public sealed class LocalKeyWrapper : IKeyWrapper
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MasterKeySize = 32;

    private readonly byte[] _masterKey;

    public LocalKeyWrapper(IOptions<SecretStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var keyRef = options.Value.KeyRef
            ?? throw new InvalidOperationException("SecretStore:KeyRef is required for LocalKey mode.");
        _masterKey = Convert.FromBase64String(keyRef);
        if (_masterKey.Length != MasterKeySize)
        {
            throw new InvalidOperationException("SecretStore:KeyRef must be a base64-encoded 32-byte key.");
        }
    }

    public Task<byte[]> WrapAsync(byte[] dataKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataKey);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[TagSize];
        using (var gcm = new AesGcm(_masterKey, TagSize))
        {
            gcm.Encrypt(nonce, dataKey, ciphertext, tag);
        }

        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
        return Task.FromResult(result);
    }

    public Task<byte[]> UnwrapAsync(byte[] wrappedDataKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(wrappedDataKey);

        var nonce = wrappedDataKey[..NonceSize];
        var tag = wrappedDataKey[NonceSize..(NonceSize + TagSize)];
        var ciphertext = wrappedDataKey[(NonceSize + TagSize)..];
        var dataKey = new byte[ciphertext.Length];
        using (var gcm = new AesGcm(_masterKey, TagSize))
        {
            gcm.Decrypt(nonce, ciphertext, tag, dataKey);
        }

        return Task.FromResult(dataKey);
    }
}
