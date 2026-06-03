using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Infrastructure;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EMaigrator.Workers.IntegrationTests;

/// <summary>
/// End-to-end functional verification of the worker pipeline against REAL infrastructure:
/// RabbitMQ bus + the five real consumers + the real IMAP connector + Postgres ledger + Redis,
/// with GreenMail as BOTH source and destination (src@ / dst@ mailboxes). No mocks of the engine.
/// </summary>
[Collection("pipeline")]
public sealed class EndToEndPipelineTests
{
    // Each test seeds into TWO per-run subfolders only (never the shared INBOX), so the source's
    // folder fan-out sees exactly this run's 20 messages and the ledger count is unpolluted.
    private const int FolderACount = 12;
    private const int FolderBCount = 8;
    private const int Total = FolderACount + FolderBCount;

    private readonly EmaigratorPipelineFixture _fx;

    public EndToEndPipelineTests(EmaigratorPipelineFixture fx) => _fx = fx;

    private static TimeSpan Timeout => TimeSpan.FromMinutes(3);

    private readonly record struct RunSeed(string Token, string FolderA, string FolderB, List<string> MessageIds);

    private async Task<RunSeed> SeedSourceAsync()
    {
        await _fx.ResetMailboxAsync(EmaigratorPipelineFixture.SrcEmail);
        await _fx.ResetMailboxAsync(EmaigratorPipelineFixture.DstEmail);
        var token = Guid.NewGuid().ToString("N")[..8];
        var folderA = $"Mail-A-{token}";
        var folderB = $"Mail-B-{token}";
        var ids = new List<string>(Total);

        for (var i = 0; i < FolderACount; i++)
        {
            var mid = $"<a-{token}-{i}@local.test>";
            ids.Add(mid.Trim('<', '>'));
            await _fx.AppendAsync(EmaigratorPipelineFixture.SrcEmail, folderA, $"msg-a-{token}-{i}", mid);
        }

        for (var i = 0; i < FolderBCount; i++)
        {
            var mid = $"<b-{token}-{i}@local.test>";
            ids.Add(mid.Trim('<', '>'));
            await _fx.AppendAsync(EmaigratorPipelineFixture.SrcEmail, folderB, $"msg-b-{token}-{i}", mid);
        }

        return new RunSeed(token, folderA, folderB, ids);
    }

    private (Guid MigrationId, MigrationConnections Conns) NewMigration(string? srcSecret, string? dstSecret)
    {
        var migrationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var conns = new MigrationConnections(
            jobId,
            "tenant-e2e",
            _fx.Descriptor(EmaigratorPipelineFixture.SrcEmail, srcSecret),
            _fx.Descriptor(EmaigratorPipelineFixture.DstEmail, dstSecret));
        return (migrationId, conns);
    }

    /// <summary>Poll until the dest mailbox contains every expected Message-ID, or time out.</summary>
    private async Task WaitForDestinationAsync(IReadOnlyCollection<string> expectedIds, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var present = await _fx.MessageIdsAsync(EmaigratorPipelineFixture.DstEmail);
            if (expectedIds.All(present.Contains))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>Poll until the ledger total (Migrated+Failed) for the migration reaches a target.</summary>
    private async Task<LedgerCounts> WaitForLedgerAsync(Guid migrationId, long targetTerminal, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        LedgerCounts counts = new(0, 0, 0, 0);
        while (sw.Elapsed < timeout)
        {
            counts = await _fx.GetLedgerCountsAsync(migrationId);
            if (counts.Migrated + counts.Failed >= targetTerminal)
            {
                return counts;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return counts;
    }

    private async Task RunPipelineToCompletionAsync(
        Guid migrationId, MigrationConnections conns, IReadOnlyCollection<string> expectedIds)
    {
        var config = _fx.BuildConfiguration();
        using var host = _fx.BuildHost(migrationId, conns, config);
        await host.StartAsync();
        try
        {
            var orchestrator = host.Services.GetRequiredService<IJobOrchestrator>();
            await orchestrator.EnqueueMigrationAsync(migrationId, CancellationToken.None);
            await WaitForDestinationAsync(expectedIds, Timeout);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Copy_correctness_all_messages_land_and_ledger_marks_migrated()
    {
        FaultInjectingMessageHydrator.PoisonEnabled = false;
        var seed = await SeedSourceAsync();
        var (migrationId, conns) = NewMigration(null, null);

        // Provide real password secrets so the session factory can authenticate.
        using var secretHost = BuildSecretHost();
        var srcSecret = await EmaigratorPipelineFixture.StorePasswordSecretAsync(secretHost, conns.TenantId);
        var dstSecret = await EmaigratorPipelineFixture.StorePasswordSecretAsync(secretHost, conns.TenantId);
        conns = conns with
        {
            Source = _fx.Descriptor(EmaigratorPipelineFixture.SrcEmail, srcSecret),
            Dest = _fx.Descriptor(EmaigratorPipelineFixture.DstEmail, dstSecret),
        };

        await RunPipelineToCompletionAsync(migrationId, conns, seed.MessageIds);

        var destIds = await _fx.MessageIdsAsync(EmaigratorPipelineFixture.DstEmail);
        seed.MessageIds.Should().OnlyContain(id => destIds.Contains(id),
            "every seeded message should land in the destination");

        var counts = await WaitForLedgerAsync(migrationId, Total, Timeout);
        counts.Migrated.Should().Be(Total);
        counts.Failed.Should().Be(0);
    }

    [Fact]
    public async Task Idempotent_rerun_produces_zero_duplicates()
    {
        FaultInjectingMessageHydrator.PoisonEnabled = false;
        var seed = await SeedSourceAsync();
        var (migrationId, conns) = NewMigration(null, null);

        using var secretHost = BuildSecretHost();
        var srcSecret = await EmaigratorPipelineFixture.StorePasswordSecretAsync(secretHost, conns.TenantId);
        var dstSecret = await EmaigratorPipelineFixture.StorePasswordSecretAsync(secretHost, conns.TenantId);
        conns = conns with
        {
            Source = _fx.Descriptor(EmaigratorPipelineFixture.SrcEmail, srcSecret),
            Dest = _fx.Descriptor(EmaigratorPipelineFixture.DstEmail, dstSecret),
        };

        // First run.
        await RunPipelineToCompletionAsync(migrationId, conns, seed.MessageIds);
        var afterFirst = await WaitForLedgerAsync(migrationId, Total, Timeout);
        afterFirst.Migrated.Should().Be(Total);
        var destCountAfterFirst = await CountSeededInDestAsync(seed.MessageIds);
        destCountAfterFirst.Should().Be(Total);

        // Second run — SAME migration id, so the ledger short-circuits each message (Skipped).
        await RunPipelineToCompletionAsync(migrationId, conns, seed.MessageIds);

        var afterSecond = await WaitForLedgerAsync(migrationId, Total, Timeout);
        afterSecond.Migrated.Should().Be(Total, "no message is migrated twice");
        var destCountAfterSecond = await CountSeededInDestAsync(seed.MessageIds);
        destCountAfterSecond.Should().Be(Total, "the re-run must not duplicate any message");
    }

    [Fact]
    public async Task Crash_resume_completes_with_zero_duplicates()
    {
        FaultInjectingMessageHydrator.PoisonEnabled = false;
        var seed = await SeedSourceAsync();
        var (migrationId, conns) = NewMigration(null, null);

        using var secretHost = BuildSecretHost();
        var srcSecret = await EmaigratorPipelineFixture.StorePasswordSecretAsync(secretHost, conns.TenantId);
        var dstSecret = await EmaigratorPipelineFixture.StorePasswordSecretAsync(secretHost, conns.TenantId);
        conns = conns with
        {
            Source = _fx.Descriptor(EmaigratorPipelineFixture.SrcEmail, srcSecret),
            Dest = _fx.Descriptor(EmaigratorPipelineFixture.DstEmail, dstSecret),
        };

        var config = _fx.BuildConfiguration();

        // Arm the interrupted-job lookup so a freshly built host re-enqueues StartMigration on boot.
        lock (TestInterruptedJobLookup.ToResume)
        {
            TestInterruptedJobLookup.ToResume.Clear();
            TestInterruptedJobLookup.ToResume.Add(migrationId);
        }

        // First host: start the migration, let ~half copy, then KILL it.
        var host1 = _fx.BuildHost(migrationId, conns, config);
        await host1.StartAsync();
        var orchestrator = host1.Services.GetRequiredService<IJobOrchestrator>();
        await orchestrator.EnqueueMigrationAsync(migrationId, CancellationToken.None);

        await WaitForLedgerAtLeastAsync(migrationId, Total / 2, Timeout);
        // Crash: stop the bus/consumers (un-acked RabbitMQ batches will redeliver). We defer
        // Dispose() until host2 has started, because MassTransit's static LogContext references
        // host1's ILoggerFactory and host2 re-points it during StartAsync; disposing host1 first
        // would tear that factory down mid-reconfiguration.
        await host1.StopAsync();

        // Restart against the SAME containers. CrashResumeStartupService re-fans-out; un-acked
        // RabbitMQ batches also redeliver. Ledger IsDone makes both paths idempotent.
        var host2 = _fx.BuildHost(migrationId, conns, config);
        await host2.StartAsync();
        host1.Dispose();
        try
        {
            await WaitForDestinationAsync(seed.MessageIds, Timeout);
        }
        finally
        {
            await host2.StopAsync();
            host2.Dispose();
            lock (TestInterruptedJobLookup.ToResume)
            {
                TestInterruptedJobLookup.ToResume.Clear();
            }
        }

        var counts = await WaitForLedgerAsync(migrationId, Total, Timeout);
        counts.Migrated.Should().Be(Total);
        var destCount = await CountSeededInDestAsync(seed.MessageIds);
        destCount.Should().Be(Total, "crash-resume must finish the copy with zero duplicates");
    }

    [Fact]
    public async Task Poison_message_dlqs_and_other_nineteen_complete()
    {
        await _fx.ResetMailboxAsync(EmaigratorPipelineFixture.SrcEmail);
        await _fx.ResetMailboxAsync(EmaigratorPipelineFixture.DstEmail);
        var token = Guid.NewGuid().ToString("N")[..8];
        var folder = $"Mail-P-{token}";
        var ids = new List<string>(Total);

        // 19 healthy + 1 poison (subject carries the marker → FaultInjectingMessageHydrator throws).
        for (var i = 0; i < Total - 1; i++)
        {
            var mid = $"<p-{token}-{i}@local.test>";
            ids.Add(mid.Trim('<', '>'));
            await _fx.AppendAsync(EmaigratorPipelineFixture.SrcEmail, folder, $"ok-{token}-{i}", mid);
        }

        var poisonMid = $"<poison-{token}@local.test>";
        await _fx.AppendAsync(EmaigratorPipelineFixture.SrcEmail, folder,
            $"{FaultInjectingMessageHydrator.PoisonMarker}-{token}", poisonMid);
        var healthyIds = ids.ToList();

        var (migrationId, conns) = NewMigration(null, null);
        using var secretHost = BuildSecretHost();
        var srcSecret = await EmaigratorPipelineFixture.StorePasswordSecretAsync(secretHost, conns.TenantId);
        var dstSecret = await EmaigratorPipelineFixture.StorePasswordSecretAsync(secretHost, conns.TenantId);
        conns = conns with
        {
            Source = _fx.Descriptor(EmaigratorPipelineFixture.SrcEmail, srcSecret),
            Dest = _fx.Descriptor(EmaigratorPipelineFixture.DstEmail, dstSecret),
        };

        FaultInjectingMessageHydrator.PoisonEnabled = true;
        try
        {
            var config = _fx.BuildConfiguration();
            using var host = _fx.BuildHost(migrationId, conns, config);
            await host.StartAsync();
            try
            {
                var orchestrator = host.Services.GetRequiredService<IJobOrchestrator>();
                await orchestrator.EnqueueMigrationAsync(migrationId, CancellationToken.None);

                // 19 migrated + 1 failed = 20 terminal ledger rows.
                await WaitForLedgerAsync(migrationId, Total, Timeout);
            }
            finally
            {
                await host.StopAsync();
            }
        }
        finally
        {
            FaultInjectingMessageHydrator.PoisonEnabled = false;
        }

        // The 19 healthy messages all landed.
        var destIds = await _fx.MessageIdsAsync(EmaigratorPipelineFixture.DstEmail);
        healthyIds.Should().OnlyContain(id => destIds.Contains(id), "the 19 non-poison messages migrate");

        // Ledger: 19 Migrated, ≥1 Failed (the poison), poison NOT Migrated.
        var counts = await _fx.GetLedgerCountsAsync(migrationId);
        counts.Migrated.Should().Be(Total - 1);
        counts.Failed.Should().BeGreaterThanOrEqualTo(1);

        // A content-free PoisonBatch NeedsDecisionEvent was published for this migration.
        CollectingNeedsDecisionConsumer.Decisions
            .Where(d => d.MailboxMigrationId == migrationId)
            .Should().Contain(d => d.IssueType == "PoisonBatch");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private IHost BuildSecretHost()
    {
        // A minimal host that only needs Infrastructure registered to resolve ISecretStore for seeding.
        var config = _fx.BuildConfiguration();
        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddInfrastructure(config, registerBus: false))
            .Build();
    }

    private async Task<int> CountSeededInDestAsync(IReadOnlyCollection<string> seededIds)
    {
        var present = await _fx.MessageIdsAsync(EmaigratorPipelineFixture.DstEmail);
        return seededIds.Count(present.Contains);
    }

    private async Task WaitForLedgerAtLeastAsync(Guid migrationId, long target, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var counts = await _fx.GetLedgerCountsAsync(migrationId);
            if (counts.Migrated + counts.Failed >= target)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }
}
