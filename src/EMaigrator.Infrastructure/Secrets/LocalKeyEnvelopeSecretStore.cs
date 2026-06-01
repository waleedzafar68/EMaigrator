using System.Text;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.Secrets;

/// <summary>
/// Envelope-encrypting credential store. A random data key encrypts the secret (AES-GCM); the data
/// key is wrapped by the master key via <see cref="IKeyWrapper"/>. Only the ciphertext envelope is
/// persisted — a DB breach yields ciphertext, never plaintext.
/// </summary>
public sealed class LocalKeyEnvelopeSecretStore : ISecretStore
{
    private readonly IDbContextFactory<EmaigratorDbContext> _factory;
    private readonly IKeyWrapper _wrapper;
    private readonly EnvelopeCipher _cipher;

    public LocalKeyEnvelopeSecretStore(
        IDbContextFactory<EmaigratorDbContext> factory,
        IKeyWrapper wrapper,
        EnvelopeCipher cipher)
    {
        _factory = factory;
        _wrapper = wrapper;
        _cipher = cipher;
    }

    public async Task<string> StoreAsync(string tenantId, string plaintext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var dataKey = _cipher.GenerateDataKey();
        var wrapped = await _wrapper.WrapAsync(dataKey, ct).ConfigureAwait(false);
        var blob = _cipher.Seal(dataKey, wrapped, Encoding.UTF8.GetBytes(plaintext));
        Array.Clear(dataKey);

        var secretRef = $"cred:{Guid.NewGuid():N}";
        await using var dbctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        dbctx.Credentials.Add(new CredentialRow
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.TryParse(tenantId, out var t) ? t : Guid.Empty,
            SecretRef = secretRef,
            CipherBlob = Convert.ToBase64String(blob),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await dbctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return secretRef;
    }

    public async Task<string> RetrieveAsync(string secretRef, CancellationToken ct)
    {
        await using var dbctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var row = await dbctx.Credentials.AsNoTracking()
            .FirstOrDefaultAsync(r => r.SecretRef == secretRef, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"No credential for secretRef '{secretRef}'.");

        var blob = Convert.FromBase64String(row.CipherBlob);
        var (wrapped, payload) = _cipher.ExtractWrappedKey(blob);
        var dataKey = await _wrapper.UnwrapAsync(wrapped, ct).ConfigureAwait(false);
        try
        {
            return Encoding.UTF8.GetString(_cipher.Open(dataKey, payload));
        }
        finally
        {
            Array.Clear(dataKey);
        }
    }

    public async Task PurgeAsync(string secretRef, CancellationToken ct)
    {
        await using var dbctx = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await dbctx.Credentials.Where(r => r.SecretRef == secretRef).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }
}
