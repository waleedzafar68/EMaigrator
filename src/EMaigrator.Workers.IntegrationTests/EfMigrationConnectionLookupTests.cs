using System.Text.Json;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Workers.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EMaigrator.Workers.IntegrationTests;

[Collection("pipeline")]
public class EfMigrationConnectionLookupTests
{
    private readonly EmaigratorPipelineFixture _fx;
    public EfMigrationConnectionLookupTests(EmaigratorPipelineFixture fx) => _fx = fx;

    private IDbContextFactory<EmaigratorDbContext> Factory()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(_fx.BuildConfiguration(), registerBus: false);
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
    }

    [Fact]
    public async Task Resolves_descriptors_from_job()
    {
        var factory = Factory();
        var jobId = Guid.NewGuid();
        var migrationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        static ConnectionDescriptor Desc(string email) => new()
        {
            Provider = new ProviderId("imap"),
            Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["accountEmail"] = email },
            SecretRef = "ref-" + email,
        };

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Jobs.Add(new Job
            {
                Id = jobId,
                TenantId = tenantId,
                SourceProvider = new ProviderId("imap"),
                DestProvider = new ProviderId("imap"),
                SourceConnectionRef = JsonSerializer.Serialize(Desc("src@x")),
                DestConnectionRef = JsonSerializer.Serialize(Desc("dst@x")),
            });
            ctx.MailboxMigrations.Add(new MailboxMigration
            {
                Id = migrationId,
                JobId = jobId,
                SourceMailbox = "src@x",
                DestMailbox = "dst@x",
                Status = MailboxMigrationStatus.Pending,
            });
            await ctx.SaveChangesAsync();
        }

        var lookup = new EfMigrationConnectionLookup(factory);
        var conns = await lookup.GetAsync(migrationId, CancellationToken.None);

        conns.JobId.Should().Be(jobId);
        conns.TenantId.Should().Be(tenantId.ToString());
        conns.Source.Settings["accountEmail"].Should().Be("src@x");
        conns.Source.SecretRef.Should().Be("ref-src@x");
        conns.Dest.Settings["accountEmail"].Should().Be("dst@x");
        conns.Dest.Provider.Should().Be(new ProviderId("imap"));
    }

    [Fact]
    public async Task Throws_when_migration_missing()
    {
        var lookup = new EfMigrationConnectionLookup(Factory());
        var act = async () => await lookup.GetAsync(Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
