using System.Security.Cryptography;
using System.Text;
using EMaigrator.Core.Configuration;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using EMaigrator.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EMaigrator.Infrastructure.IntegrationTests.Secrets;

[Collection("postgres")]
public class LocalKeyEnvelopeSecretStoreTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;

    public LocalKeyEnvelopeSecretStoreTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> DbOptions =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private sealed class Factory(DbContextOptions<EmaigratorDbContext> o) : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(o);
    }

    private LocalKeyEnvelopeSecretStore NewStore()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var opts = Options.Create(new SecretStoreOptions { Mode = "LocalKey", KeyRef = key });
        return new LocalKeyEnvelopeSecretStore(new Factory(DbOptions), new LocalKeyWrapper(opts), new EnvelopeCipher());
    }

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(DbOptions);
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Store_then_retrieve_roundtrips()
    {
        var store = NewStore();
        var tenant = Guid.NewGuid().ToString();
        const string secret = "imap-app-password-Sup3rSecret!";

        var secretRef = await store.StoreAsync(tenant, secret, default);
        secretRef.Should().NotBeNullOrWhiteSpace();

        var back = await store.RetrieveAsync(secretRef, default);
        back.Should().Be(secret);
    }

    [Fact]
    public async Task Stored_blob_is_ciphertext_not_plaintext()
    {
        var store = NewStore();
        const string secret = "PLAINTEXT-CANARY-9f2a";
        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), secret, default);

        await using var ctx = new EmaigratorDbContext(DbOptions);
        var row = await ctx.Credentials.SingleAsync(r => r.SecretRef == secretRef);

        row.CipherBlob.Should().NotContain(secret);
        Encoding.UTF8.GetString(Convert.FromBase64String(row.CipherBlob))
            .Should().NotContain(secret);
    }

    [Fact]
    public async Task Tampered_ciphertext_fails_to_decrypt()
    {
        var store = NewStore();
        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), "secret", default);

        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            var row = await ctx.Credentials.SingleAsync(r => r.SecretRef == secretRef);
            var bytes = Convert.FromBase64String(row.CipherBlob);
            bytes[^1] ^= 0xFF; // flip a tag byte
            row.CipherBlob = Convert.ToBase64String(bytes);
            await ctx.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<AuthenticationTagMismatchException>(
            () => store.RetrieveAsync(secretRef, default));
    }

    [Fact]
    public async Task Purge_removes_secret()
    {
        var store = NewStore();
        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), "secret", default);

        await store.PurgeAsync(secretRef, default);

        await using var ctx = new EmaigratorDbContext(DbOptions);
        (await ctx.Credentials.AnyAsync(r => r.SecretRef == secretRef)).Should().BeFalse();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RetrieveAsync(secretRef, default));
    }
}
