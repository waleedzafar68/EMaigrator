using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Infrastructure.IntegrationTests.Fixtures;
using EMaigrator.Infrastructure.Retention;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace EMaigrator.Infrastructure.IntegrationTests.Retention;

[Collection("postgres")]
public class CredentialPurgeHookTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;

    public CredentialPurgeHookTests(PostgresFixture pg) => _pg = pg;

    private DbContextOptions<EmaigratorDbContext> DbOptions =>
        new DbContextOptionsBuilder<EmaigratorDbContext>().UseNpgsql(_pg.ConnectionString).Options;

    private sealed class Factory(DbContextOptions<EmaigratorDbContext> options)
        : IDbContextFactory<EmaigratorDbContext>
    {
        public EmaigratorDbContext CreateDbContext() => new(options);
    }

    public async Task InitializeAsync()
    {
        await using var ctx = new EmaigratorDbContext(DbOptions);
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Purges_credentials_when_job_terminal()
    {
        var tenant = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            ctx.Jobs.Add(new Job
            {
                Id = jobId,
                TenantId = tenant,
                SourceProvider = new ProviderId("imap"),
                DestProvider = new ProviderId("graph"),
                SourceConnectionRef = "cred:src",
                DestConnectionRef = "cred:dst",
                Status = JobStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            ctx.Credentials.Add(new CredentialRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                SecretRef = "cred:src",
                CipherBlob = "x",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            ctx.Credentials.Add(new CredentialRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                SecretRef = "cred:dst",
                CipherBlob = "y",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var secretStore = Substitute.For<ISecretStore>();
        var hook = new CredentialPurgeHook(new Factory(DbOptions), secretStore);

        await hook.PurgeForJobAsync(jobId, default);

        await using var verify = new EmaigratorDbContext(DbOptions);
        (await verify.Credentials.AnyAsync(c => c.TenantId == tenant)).Should().BeFalse();
        await secretStore.Received(1).PurgeAsync("cred:src", Arg.Any<CancellationToken>());
        await secretStore.Received(1).PurgeAsync("cred:dst", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Noop_when_job_not_terminal()
    {
        var jobId = Guid.NewGuid();
        await using (var ctx = new EmaigratorDbContext(DbOptions))
        {
            ctx.Jobs.Add(new Job
            {
                Id = jobId,
                TenantId = Guid.NewGuid(),
                SourceProvider = new ProviderId("imap"),
                DestProvider = new ProviderId("graph"),
                SourceConnectionRef = "cred:a",
                Status = JobStatus.Running,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            ctx.Credentials.Add(new CredentialRow
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                SecretRef = "cred:a",
                CipherBlob = "x",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        var hook = new CredentialPurgeHook(new Factory(DbOptions), Substitute.For<ISecretStore>());
        await hook.PurgeForJobAsync(jobId, default);

        await using var verify = new EmaigratorDbContext(DbOptions);
        (await verify.Credentials.AnyAsync(c => c.SecretRef == "cred:a"))
            .Should().BeTrue("running job keeps creds");
    }
}
