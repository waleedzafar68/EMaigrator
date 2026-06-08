using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using EMaigrator.Infrastructure.Observability;
using EMaigrator.Infrastructure.Retention;
using EMaigrator.Infrastructure.Secrets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using Serilog.Sinks.InMemory;

namespace EMaigrator.Infrastructure.IntegrationTests.Security;

[Collection("postgres")]
public class InfrastructureSecurityTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;

    public InfrastructureSecurityTests(PostgresFixture pg) => _pg = pg;

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
    public async Task Credential_blob_is_ciphertext_via_raw_sql()
    {
        const string canary = "PLAINTEXT-CANARY-c0ffee";
        var store = NewStore();
        var secretRef = await store.StoreAsync(Guid.NewGuid().ToString(), canary, default);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT ""CipherBlob"" FROM credentials WHERE ""SecretRef"" = @r", conn);
        cmd.Parameters.AddWithValue("r", secretRef);
        var blob = (string)(await cmd.ExecuteScalarAsync())!;

        blob.Should().NotContain(canary, $"raw DB value must be ciphertext: {blob}");
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(blob));
        decoded.Should().NotContain(canary, "base64-decoded blob must not reveal plaintext");
    }

    [Theory]
    [InlineData("ledger_entries", new[] { "body", "attachment", "content", "raw", "mime" })]
    [InlineData("migration_logs", new[] { "body", "attachment", "content", "raw", "mime", "sender", "recipient", "from", "to", "cc", "bcc", "address" })]
    // jobs + mailbox_migrations are written by the reconcile path (Job.Mode, MailboxMigration.Status/counts).
    // Their mailbox-address columns are connection METADATA (which account), not message content, so only
    // the body/byte tokens are forbidden here — never a body/attachment/content/raw/mime column.
    [InlineData("jobs", new[] { "body", "attachment", "content", "raw", "mime" })]
    [InlineData("mailbox_migrations", new[] { "body", "attachment", "content", "raw", "mime" })]
    public async Task Metadata_tables_have_no_forbidden_columns(string table, string[] forbidden)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_name = @t", conn);
        cmd.Parameters.AddWithValue("t", table);
        var cols = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            cols.Add(reader.GetString(0));
        }

        var offending = cols.Where(c => forbidden.Any(f => c.Contains(f, StringComparison.OrdinalIgnoreCase))).ToArray();
        offending.Should().BeEmpty($"{table} columns: [{string.Join(", ", cols)}]");
    }

    [Fact]
    public async Task Job_Mode_is_an_allowed_non_content_metadata_column()
    {
        // Reconcile (Plan 11) added Job.Mode. Explicitly allow-list it: it MUST exist (so the reconcile
        // endpoint/worker can mark a run) and it MUST be plain metadata — its name carries none of the
        // body/attachment/content/raw/mime tokens, and it stores an enum string, not message content.
        string[] forbidden = ["body", "attachment", "content", "raw", "mime"];
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT data_type FROM information_schema.columns WHERE table_name = 'jobs' AND column_name = 'Mode'", conn);
        var dataType = (string?)await cmd.ExecuteScalarAsync();

        dataType.Should().NotBeNull("the jobs table must have a Mode column after the AddJobMode migration");
        forbidden.Any(f => "Mode".Contains(f, StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse("Job.Mode is metadata (a JobMode enum), never message content");
    }

    [Fact]
    public void Logs_contain_zero_plaintext_credentials()
    {
        const string canary = "LOG-CANARY-s3cr3t";
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.With(new SecretScrubbingEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("auth {Password} {ClientSecret} {CipherBlob} for {SourceFolder}", canary, canary, canary, "INBOX");

        var allText = string.Join("\n", sink.LogEvents.Select(e =>
            e.RenderMessage(CultureInfo.InvariantCulture) + "|" + string.Join(
                ",",
                e.Properties.Select(p => p.Value.ToString(null, CultureInfo.InvariantCulture)))));
        allText.Should().NotContain(canary, $"no plaintext credential may appear in logs. Captured: {allText}");
    }

    [Fact]
    public async Task Credentials_purged_on_terminal_state()
    {
        var store = NewStore();
        var tenant = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var secretRef = await store.StoreAsync(tenant.ToString(), "to-be-purged", default);

        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            ctx.Jobs.Add(new Job
            {
                Id = jobId,
                TenantId = tenant,
                SourceProvider = new ProviderId("imap"),
                DestProvider = new ProviderId("graph"),
                SourceConnectionRef = secretRef,
                Status = JobStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var hook = new CredentialPurgeHook(new Factory(DbOptions), store);
        await hook.PurgeForJobAsync(jobId, default);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT COUNT(*) FROM credentials WHERE ""SecretRef"" = @r", conn);
        cmd.Parameters.AddWithValue("r", secretRef);
        ((long)(await cmd.ExecuteScalarAsync())!).Should().Be(0);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RetrieveAsync(secretRef, default));
    }

    [Fact]
    public async Task Logs_do_not_linger_past_retention()
    {
        var now = DateTimeOffset.UtcNow;
        var mig = Guid.NewGuid();
        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            ctx.MigrationLogs.Add(new MigrationLogRow { MailboxMigrationId = mig, SourceFolder = "f", DestFolder = "f", Status = "Migrated", CreatedAt = now.AddDays(-31) });
            ctx.MigrationLogs.Add(new MigrationLogRow { MailboxMigrationId = mig, SourceFolder = "f", DestFolder = "f", Status = "Migrated", CreatedAt = now.AddDays(-1) });
            await ctx.SaveChangesAsync();
        }

        var svc = new LogRetentionPurgeService(
            new Factory(DbOptions),
            Options.Create(new RetentionOptions { LogRetentionDays = 30 }),
            NullLogger<LogRetentionPurgeService>.Instance);
        var deleted = await svc.PurgeOnceAsync(now, default);
        deleted.Should().Be(1, "exactly the over-retention row must be purged");

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT COUNT(*) FROM migration_logs WHERE ""MailboxMigrationId"" = @m", conn);
        cmd.Parameters.AddWithValue("m", mig);
        ((long)(await cmd.ExecuteScalarAsync())!).Should().Be(1, "only the within-retention row survives");
    }
}
