using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using EMaigrator.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Infrastructure.IntegrationTests.Secrets;

[Collection("postgres")]
public class KmsEnvelopeSecretStoreTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;

    public KmsEnvelopeSecretStoreTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> DbOptions =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private sealed class Factory(DbContextOptions<EmaigratorDbContext> o) : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(o);
    }

    /// <summary>In-memory KMS double: deterministic reversible wrap so the local process never holds the master key.</summary>
    private sealed class FakeKmsClient : IKmsClient
    {
        private readonly byte[] _kek = Enumerable.Range(0, 32).Select(i => (byte)((i * 7) + 3)).ToArray();

        public int WrapCalls { get; private set; }

        public Task<byte[]> WrapKeyAsync(byte[] key, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(key);
            WrapCalls++;
            return Task.FromResult(key.Select((b, i) => (byte)(b ^ _kek[i % _kek.Length])).ToArray());
        }

        public Task<byte[]> UnwrapKeyAsync(byte[] wrapped, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(wrapped);
            return Task.FromResult(wrapped.Select((b, i) => (byte)(b ^ _kek[i % _kek.Length])).ToArray());
        }
    }

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(DbOptions);
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Store_retrieve_roundtrips_through_kms_wrapper()
    {
        var kms = new FakeKmsClient();
        var store = new LocalKeyEnvelopeSecretStore(new Factory(DbOptions), new KmsKeyWrapper(kms), new EnvelopeCipher());

        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), "kms-protected-secret", default);
        (await store.RetrieveAsync(secretRef, default)).Should().Be("kms-protected-secret");
        kms.WrapCalls.Should().Be(1, "the data key is wrapped exactly once per stored secret");
    }

    [Fact]
    public async Task Blob_remains_ciphertext_under_kms_wrapping()
    {
        var kms = new FakeKmsClient();
        var store = new LocalKeyEnvelopeSecretStore(new Factory(DbOptions), new KmsKeyWrapper(kms), new EnvelopeCipher());
        const string secret = "KMS-CANARY-771a";

        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), secret, default);

        await using var ctx = new EmaigratorDbContext(DbOptions);
        var row = await ctx.Credentials.SingleAsync(r => r.SecretRef == secretRef);
        row.CipherBlob.Should().NotContain(secret);
    }

    [Fact]
    public async Task Purge_then_retrieve_throws()
    {
        var store = new LocalKeyEnvelopeSecretStore(new Factory(DbOptions), new KmsKeyWrapper(new FakeKmsClient()), new EnvelopeCipher());
        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), "s", default);
        await store.PurgeAsync(secretRef, default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RetrieveAsync(secretRef, default));
    }
}
