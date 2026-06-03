using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Infrastructure;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Workers.IntegrationTests.Security;

/// <summary>
/// Security gate (b): proves the DLQ decision payload carries NO message content. Forces a poison
/// message whose BODY holds a body sentinel and whose SUBJECT holds a subject sentinel, drives it
/// through the real pipeline to the fault path, captures the resulting NeedsDecisionEvent, serializes
/// it to JSON, and asserts the JSON contains NEITHER sentinel — only identity keys (refs), folder,
/// and error type.
/// </summary>
[Trait("Category", "Security")]
[Collection("pipeline")]
public sealed class DlqContentFreeTests
{
    private const int MessageCount = 6;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly EmaigratorPipelineFixture _fx;
    private readonly ITestOutputHelper _out;

    public DlqContentFreeTests(EmaigratorPipelineFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private static TimeSpan Timeout => TimeSpan.FromMinutes(3);

    [Fact]
    public async Task Dlq_decision_event_contains_no_body_or_subject_content()
    {
        var bodySentinel = $"EMAIGRATOR_BODY_SENTINEL_{Guid.NewGuid():N}";
        var subjectSentinel = $"EMAIGRATOR_SUBJECT_SENTINEL_{Guid.NewGuid():N}";

        _ = await _fx.SeedSourceWithOnePoisonSentinelAsync(
            MessageCount, 0, bodySentinel, subjectSentinel);

        var migrationId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var conns = new MigrationConnections(
            jobId,
            "tenant-sec-dlq",
            _fx.Descriptor(EmaigratorPipelineFixture.SrcEmail, null),
            _fx.Descriptor(EmaigratorPipelineFixture.DstEmail, null));

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

                // MessageCount terminal ledger rows = (MessageCount-1) migrated + 1 failed.
                await WaitForLedgerAsync(migrationId, MessageCount, Timeout);
                await WaitForDecisionAsync(migrationId, Timeout);
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

        var decisions = CollectingNeedsDecisionConsumer.Decisions
            .Where(d => d.MailboxMigrationId == migrationId && d.IssueType == "PoisonBatch")
            .ToList();

        decisions.Should().NotBeEmpty("the poison message must produce a PoisonBatch NeedsDecisionEvent");

        var json = JsonSerializer.Serialize(decisions, JsonOptions);

        _out.WriteLine("=== DlqContentFree security evidence ===");
        _out.WriteLine($"bodySentinel    = {bodySentinel}");
        _out.WriteLine($"subjectSentinel = {subjectSentinel}");
        _out.WriteLine($"captured PoisonBatch decisions for migration = {decisions.Count}");
        _out.WriteLine("serialized NeedsDecisionEvent payload(s):");
        _out.WriteLine(json);
        _out.WriteLine(
            !json.Contains(bodySentinel, StringComparison.Ordinal) &&
            !json.Contains(subjectSentinel, StringComparison.Ordinal)
                ? "RESULT: PASS — DLQ payload free of body and subject content."
                : "RESULT: FAIL — message content leaked into DLQ payload (SECURITY FINDING).");

        // ── Assertions (not weakened) ─────────────────────────────────────────────────────────
        json.Should().NotContain(bodySentinel, "the DLQ payload must not carry the message body");
        json.Should().NotContain(subjectSentinel, "the DLQ payload must not carry the message subject");

        // The Detail is the content-free identity/folder/error envelope.
        foreach (var d in decisions)
        {
            d.Detail.Should().Contain("folder=");
            d.Detail.Should().Contain("errorType=");
            d.Detail.Should().Contain("refs=");
            d.Detail.Should().NotContain(bodySentinel);
            d.Detail.Should().NotContain(subjectSentinel);
        }
    }

    private static async Task WaitForDecisionAsync(Guid migrationId, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (CollectingNeedsDecisionConsumer.Decisions.Any(
                    d => d.MailboxMigrationId == migrationId && d.IssueType == "PoisonBatch"))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    private async Task WaitForLedgerAsync(Guid migrationId, long targetTerminal, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var counts = await _fx.GetLedgerCountsAsync(migrationId);
            if (counts.Migrated + counts.Failed >= targetTerminal)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    private IHost BuildSecretHost()
    {
        var config = _fx.BuildConfiguration();
        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddInfrastructure(config, registerBus: false))
            .Build();
    }
}
