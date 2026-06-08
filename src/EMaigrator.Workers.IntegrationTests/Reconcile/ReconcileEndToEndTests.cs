using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Workers.IntegrationTests.Reconcile;

/// <summary>
/// Functional gate (USER-GATE): proves a full reconcile makes the destination match the source and is
/// idempotent. Seed: dest has A complete, B missing one attachment, C absent. Source has A, B (with the
/// attachment), C. A real ReconcileConsumer runs over live Postgres/RabbitMQ/Redis (provider boundary
/// faked + stateful). Expect: A untouched (0 writes), B backfilled with exactly its missing attachment,
/// C copied once, NO duplicate; a second run over the now-matched destination performs 0 copies and 0
/// backfills (idempotent). Terminal counts: copied=1, backfilled=1, complete=1, failed=0.
/// </summary>
[Trait("Category", "Functional")]
[Collection("pipeline")]
public sealed class ReconcileEndToEndTests
{
    private readonly EmaigratorPipelineFixture _fx;
    private readonly ITestOutputHelper _out;

    public ReconcileEndToEndTests(EmaigratorPipelineFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    [Fact(Timeout = 300_000)]
    public async Task Reconcile_copies_missing_backfills_incomplete_skips_complete_and_is_idempotent()
    {
        var att1 = new CanonicalAttachmentInfo("a1.pdf", "application/pdf", 10);
        var att2 = new CanonicalAttachmentInfo("a2.png", "image/png", 20);

        var source = new FakeReconcileSource(new[]
        {
            new FakeSourceMessage("<a@x>", new[] { att1 }, Encoding.ASCII.GetBytes("A body")),
            new FakeSourceMessage("<b@x>", new[] { att1, att2 }, Encoding.ASCII.GetBytes("B body + attachment payload")),
            new FakeSourceMessage("<c@x>", Array.Empty<CanonicalAttachmentInfo>(), Encoding.ASCII.GetBytes("C body")),
        });
        var dest = new StatefulReconcileDestination();
        dest.Seed("<a@x>", att1);  // A complete (matches source)
        dest.Seed("<b@x>", att1);  // B present but missing att2
        // C absent → must be copied

        var config = _fx.BuildConfiguration(batchSize: 1);
        var jobId = Guid.NewGuid();
        var migrationId = Guid.NewGuid();
        using var host = ReconcileHost.Build(_fx, config, ReconcileHost.MakeConns(jobId), source, dest);
        await host.StartAsync();
        try
        {
            await ReconcileHost.PersistJobAsync(host, jobId, migrationId);
            var ledger = host.Services.GetRequiredService<ILedger>();
            var orchestrator = host.Services.GetRequiredService<IJobOrchestrator>();

            // ── Run 1 ───────────────────────────────────────────────────────────────────────────
            await orchestrator.EnqueueReconcileAsync(migrationId, CancellationToken.None);
            (await ReconcileHost.WaitTerminalAsync(host, migrationId)).Should().Be(MailboxMigrationStatus.Completed);

            // C copied EXACTLY once (no duplicate), B backfilled with EXACTLY its missing attachment.
            dest.WrittenMessageIds.Should().ContainSingle().Which.Should().Be("c@x");
            dest.WrittenMessageIds.Distinct().Should().HaveCount(1, "C must not be duplicated");
            dest.WrittenMessageIds.Should().NotContain("a@x").And.NotContain("b@x", "A and B already exist → never re-copied");
            dest.Backfills.Should().ContainSingle();
            dest.Backfills[0].Added.Should().ContainSingle().Which.Should().Be("a2.png");

            dest.AttachmentCountOf("<a@x>").Should().Be(1, "A was complete → untouched");
            dest.AttachmentCountOf("<b@x>").Should().Be(2, "B now has both attachments after backfill");
            dest.AttachmentCountOf("<c@x>").Should().Be(0, "C was copied (it has no attachments)");

            var counts1 = await ledger.GetCountsAsync(migrationId, CancellationToken.None);
            counts1.Migrated.Should().Be(1, "exactly one whole message (C) was copied");
            counts1.Failed.Should().Be(0);

            _out.WriteLine("=== Reconcile E2E run 1 ===");
            _out.WriteLine($"copied(C)={dest.WrittenMessageIds.Count}  backfilled(B)={dest.Backfills.Count}  " +
                $"complete(A,untouched)=1  A.atts={dest.AttachmentCountOf("<a@x>")}  B.atts={dest.AttachmentCountOf("<b@x>")}  " +
                $"ledger.Migrated={counts1.Migrated}  failed={counts1.Failed}");

            // ── Run 2: idempotent re-run over the now-matched destination ─────────────────────────
            await ReconcileHost.ResetToPendingAsync(host, migrationId);
            await orchestrator.EnqueueReconcileAsync(migrationId, CancellationToken.None);
            (await ReconcileHost.WaitTerminalAsync(host, migrationId)).Should().Be(MailboxMigrationStatus.Completed);

            dest.WrittenMessageIds.Should().HaveCount(1, "re-run copies NOTHING new (destination already matched)");
            dest.Backfills.Should().HaveCount(1, "re-run backfills NOTHING new");
            dest.AttachmentCountOf("<b@x>").Should().Be(2, "no double-backfill on re-run");

            _out.WriteLine("=== Reconcile E2E run 2 (idempotent) ===");
            _out.WriteLine($"copied={dest.WrittenMessageIds.Count}  backfilled={dest.Backfills.Count}  (unchanged → idempotent, no duplicates)");
            _out.WriteLine("RESULT: PASS — copy/backfill/skip classified correctly; no duplicates; idempotent re-run.");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
