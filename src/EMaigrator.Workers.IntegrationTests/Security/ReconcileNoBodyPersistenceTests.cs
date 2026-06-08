using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using EMaigrator.Workers.IntegrationTests.Reconcile;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Workers.IntegrationTests.Security;

/// <summary>
/// Security gate (USER-GATE): proves the RECONCILE path persists ZERO message-body / attachment bytes
/// to Postgres — only metadata, identity, status and counts. A real ReconcileConsumer runs over the
/// live containers (real Postgres ledger + EF status writer + Redis + RabbitMQ); only the provider
/// boundary is faked. One message is COPIED (its body carries a body-canary) and one is BACKFILLED (its
/// source carries an attachment-canary), so both canaries genuinely transit memory during the run.
/// Then every text/varchar/char/json/jsonb/bytea column of every public table is scanned for either
/// canary — any match is a real finding; the assertion is not weakened.
/// </summary>
[Trait("Category", "Security")]
[Collection("pipeline")]
public sealed class ReconcileNoBodyPersistenceTests
{
    private readonly EmaigratorPipelineFixture _fx;
    private readonly ITestOutputHelper _out;

    public ReconcileNoBodyPersistenceTests(EmaigratorPipelineFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    [Fact(Timeout = 240_000)]
    public async Task Reconcile_persists_no_body_or_attachment_bytes()
    {
        var bodyCanary = $"EMAIGRATOR_RECON_BODY_{Guid.NewGuid():N}";
        var attCanary = $"EMAIGRATOR_RECON_ATT_{Guid.NewGuid():N}";

        var att1 = new CanonicalAttachmentInfo("a1.pdf", "application/pdf", 10);
        var att2 = new CanonicalAttachmentInfo("a2.png", "image/png", 20);

        // B: present at dest but missing att2 → backfill (reads the attachment-canary source).
        // C: absent at dest → copied whole (its body carries the body-canary).
        var source = new FakeReconcileSource(new[]
        {
            new FakeSourceMessage("<b@x>", new[] { att1, att2 }, Encoding.ASCII.GetBytes($"attachment payload :: {attCanary} :: end")),
            new FakeSourceMessage("<c@x>", Array.Empty<CanonicalAttachmentInfo>(), Encoding.ASCII.GetBytes($"message body :: {bodyCanary} :: end")),
        });
        var dest = new StatefulReconcileDestination();
        dest.Seed("<b@x>", att1);

        var config = _fx.BuildConfiguration(batchSize: 1);
        var jobId = Guid.NewGuid();
        var migrationId = Guid.NewGuid();
        using var host = ReconcileHost.Build(_fx, config, migrationId, ReconcileHost.MakeConns(jobId), source, dest);
        await host.StartAsync();
        try
        {
            await ReconcileHost.PersistJobAsync(host, jobId, migrationId);

            await host.Services.GetRequiredService<IJobOrchestrator>()
                .EnqueueReconcileAsync(migrationId, CancellationToken.None);

            var status = await ReconcileHost.WaitTerminalAsync(host, migrationId);
            status.Should().Be(MailboxMigrationStatus.Completed);

            // The canaries DID transit memory — C copied (body) + B backfilled (attachment) — so the scan
            // below tests a REAL run, not a no-op.
            dest.WrittenMessageIds.Should().ContainSingle("C (absent) is copied");
            dest.Backfills.Should().ContainSingle("B (incomplete) is backfilled");

            var matches = await ReconcileHost.ScanPostgresForAsync(_fx.PostgresConnectionString, bodyCanary, attCanary);

            _out.WriteLine("=== Reconcile NoBodyPersistence security evidence ===");
            _out.WriteLine($"bodyCanary               = {bodyCanary} (in the COPIED message body)");
            _out.WriteLine($"attCanary                = {attCanary} (in the BACKFILLED attachment source)");
            _out.WriteLine($"MailboxMigration.Status  = {status}");
            _out.WriteLine($"messages copied          = {dest.WrittenMessageIds.Count}; backfills = {dest.Backfills.Count}");
            _out.WriteLine($"postgres columns matched = {matches.Count}");
            foreach (var m in matches)
            {
                _out.WriteLine($"  MATCH {m}");
            }

            _out.WriteLine(matches.Count == 0
                ? "RESULT: PASS — zero body/attachment bytes found in Postgres along the reconcile path."
                : "RESULT: FAIL — a canary was located (SECURITY FINDING).");

            matches.Should().BeEmpty(
                "reconcile persists only metadata/identity/status/counts — never body/attachment bytes (DESIGN rule #6)");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
