using System.Security.Cryptography;

namespace EMaigrator.Infrastructure.Secrets;

/// <summary>
/// AES-256-GCM data-key encryption of a plaintext payload. Output layout:
/// [4-byte wrappedKeyLen][wrappedKey][12-byte nonce][16-byte tag][ciphertext].
/// </summary>
public sealed class EnvelopeCipher
{
    // Instance fields (not consts) so the cipher carries its parameterisation and the
    // members legitimately depend on instance state — the type is composed/injected as an instance.
    private readonly int _lengthPrefixSize = sizeof(int);
    private readonly int _nonceSize = 12;
    private readonly int _tagSize = 16;
    private readonly int _dataKeySize = 32;

    public byte[] GenerateDataKey() => RandomNumberGenerator.GetBytes(_dataKeySize);

    public byte[] Seal(byte[] dataKey, byte[] wrappedDataKey, byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(wrappedDataKey);
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(_nonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[_tagSize];
        using (var gcm = new AesGcm(dataKey, _tagSize))
        {
            gcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(wrappedDataKey.Length);
            w.Write(wrappedDataKey);
            w.Write(nonce);
            w.Write(tag);
            w.Write(ciphertext);
            w.Flush();
        }

        return ms.ToArray();
    }

    public (byte[] WrappedDataKey, byte[] Payload) ExtractWrappedKey(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);

        using var ms = new MemoryStream(blob);
        using var r = new BinaryReader(ms);
        var len = r.ReadInt32();
        var wrapped = r.ReadBytes(len);
        var rest = r.ReadBytes(blob.Length - _lengthPrefixSize - len);
        return (wrapped, rest);
    }

    public byte[] Open(byte[] dataKey, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var nonce = payload[.._nonceSize];
        var tag = payload[_nonceSize..(_nonceSize + _tagSize)];
        var ciphertext = payload[(_nonceSize + _tagSize)..];
        var plaintext = new byte[ciphertext.Length];
        using var gcm = new AesGcm(dataKey, _tagSize);
        gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
