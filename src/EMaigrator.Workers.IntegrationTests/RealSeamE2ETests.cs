using System.Text.Json;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EMaigrator.Workers.IntegrationTests;

/// <summary>
/// Functional verification (USER-GATE) of the REAL engine: production seams only — the EF-backed
/// connection lookup reads a persisted Job + MailboxMigration, the real IMAP ref-lister/hydrator
/// stream the messages, and the EF status writer + MigrationCompletionConsumer drive the migration
/// to a terminal MailboxMigration.Status. No per-message test doubles are used.
/// </summary>
[Collection("pipeline")]
public sealed class RealSeamE2ETests
{
    private readonly EmaigratorPipelineFixture _fx;
    public RealSeamE2ETests(EmaigratorPipelineFixture fx) => _fx = fx;

    [Fact]
    public async Task Migrates_twenty_messages_and_writes_terminal_completed()
    {
        await _fx.ResetMailboxAsync(EmaigratorPipelineFixture.SrcEmail);
        await _fx.ResetMailboxAsync(EmaigratorPipelineFixture.DstEmail);
        var token = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 20; i++)
        {
            await _fx.AppendAsync(EmaigratorPipelineFixture.SrcEmail, "INBOX",
                $"seed-{token}-{i}", $"<{token}-{i}@local.test>");
        }

        var config = _fx.BuildConfiguration(batchSize: 5);
        using var host = _fx.BuildHostWithRealSeams(config);
        await host.StartAsync();
        try
        {
            // Store secrets ({"password":"pw"}) and persist a Job + MailboxMigration with REAL descriptors;
            // EfMigrationConnectionLookup will read these to build the live source/dest providers.
            var srcRef = await EmaigratorPipelineFixture.StorePasswordSecretAsync(host, "self-host");
            var dstRef = await EmaigratorPipelineFixture.StorePasswordSecretAsync(host, "self-host");
            var srcDesc = _fx.Descriptor(EmaigratorPipelineFixture.SrcEmail, srcRef);
            var dstDesc = _fx.Descriptor(EmaigratorPipelineFixture.DstEmail, dstRef);

            var jobId = Guid.NewGuid();
            var migrationId = Guid.NewGuid();
            var factory = host.Services.GetRequiredService<IDbContextFactory<EmaigratorDbContext>>();
            await using (var ctx = await factory.CreateDbContextAsync())
            {
                ctx.Jobs.Add(new Job
                {
                    Id = jobId,
                    TenantId = Guid.NewGuid(),
                    SourceProvider = new ProviderId("imap"),
                    DestProvider = new ProviderId("imap"),
                    SourceConnectionRef = JsonSerializer.Serialize(srcDesc),
                    DestConnectionRef = JsonSerializer.Serialize(dstDesc),
                });
                ctx.MailboxMigrations.Add(new MailboxMigration
                {
                    Id = migrationId,
                    JobId = jobId,
                    SourceMailbox = EmaigratorPipelineFixture.SrcEmail,
                    DestMailbox = EmaigratorPipelineFixture.DstEmail,
                    Status = MailboxMigrationStatus.Pending,
                });
                await ctx.SaveChangesAsync();
            }

            await host.Services.GetRequiredService<IJobOrchestrator>()
                .EnqueueMigrationAsync(migrationId, CancellationToken.None);

            // Wait (bounded ~3 min) for the migration to reach a terminal status.
            MailboxMigration row = null!;
            for (var attempt = 0; attempt < 180; attempt++)
            {
                await Task.Delay(1000);
                await using var ctx = await factory.CreateDbContextAsync();
                row = await ctx.MailboxMigrations.AsNoTracking().FirstAsync(m => m.Id == migrationId);
                if (row.Status is MailboxMigrationStatus.Completed
                    or MailboxMigrationStatus.Partial or MailboxMigrationStatus.Failed)
                {
                    break;
                }
            }

            row.Status.Should().Be(MailboxMigrationStatus.Completed);
            row.MigratedCount.Should().Be(20);
            row.FailedCount.Should().Be(0);
            row.FinishedAt.Should().NotBeNull();

            (await _fx.CountAllAsync(EmaigratorPipelineFixture.DstEmail)).Should().Be(20);

            var counts = await _fx.GetLedgerCountsAsync(migrationId);
            counts.Migrated.Should().Be(20);
            counts.Pending.Should().Be(0);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
