using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Copy;
using EMaigrator.Workers.Persistence;
using EMaigrator.Workers.Remediation;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Consumers;

public sealed class ReconcileConsumerTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    private static readonly CanonicalAttachmentInfo Att1 = new("a1.pdf", "application/pdf", 10);
    private static readonly CanonicalAttachmentInfo Att2 = new("a2.png", "image/png", 20);

    private static CanonicalMessage Msg(string messageId, params CanonicalAttachmentInfo[] attachments) => new()
    {
        IdentityKey = "mid:" + messageId,
        MessageId = messageId,
        InternalDate = DateTimeOffset.UnixEpoch,
        Attachments = attachments,
        OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.ASCII.GetBytes("raw"))),
    };

    private static async IAsyncEnumerable<DestMessageDigest> Digests(params DestMessageDigest[] items)
    {
        foreach (var d in items)
        {
            yield return d;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<CanonicalMessage> Messages(params CanonicalMessage[] items)
    {
        foreach (var m in items)
        {
            yield return m;
        }

        await Task.CompletedTask;
    }

    private static IMigrationConnectionLookup Lookup()
    {
        var lookup = Substitute.For<IMigrationConnectionLookup>();
        lookup.GetAsync(Mid, Arg.Any<CancellationToken>()).Returns(new MigrationConnections(
            JobId, "t1",
            new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
            new ConnectionDescriptor
            {
                Provider = new("graph"),
                Auth = AuthMethod.GraphAppOAuth,
                Settings = new Dictionary<string, string> { ["accountEmail"] = "dest@contoso.com" },
            }));
        return lookup;
    }

    private static (IProviderSessionFactory sessions, IDestinationProvider dest) Sessions(
        ISourceProvider src, IDestinationProvider dst)
    {
        var sessions = Substitute.For<IProviderSessionFactory>();
        sessions.CreateSourceAsync(Mid, Arg.Any<CancellationToken>()).Returns(src);
        sessions.CreateDestinationAsync(Mid, Arg.Any<CancellationToken>()).Returns(dst);
        return (sessions, dst);
    }

    private static ServiceProvider BuildHarness(
        IProviderSessionFactory sessions, IMigrationConnectionLookup lookup,
        ILedger ledger, IRateLimiter limiter, IMigrationStatusWriter status,
        IRemediationPlanStore? plans = null)
    {
        plans ??= Substitute.For<IRemediationPlanStore>();
        plans.GetApprovedAsync(Mid, Arg.Any<CancellationToken>()).Returns(new List<ApprovedRemediation>());

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton(sessions).AddSingleton(lookup).AddSingleton(ledger)
            .AddSingleton(limiter).AddSingleton(status).AddSingleton(plans)
            .AddSingleton(Substitute.For<IJobStatusFinalizer>())
            .AddSingleton<StreamingCopierFactory>()
            .AddMassTransitTestHarness(x => x.AddConsumer<ReconcileConsumer>())
            .BuildServiceProvider(true);
    }

    private static IDestinationProvider ReconcilableDest()
    {
        var dst = Substitute.For<IDestinationProvider, IReconcilableDestination>();
        dst.Id.Returns(new ProviderId("graph"));
        dst.Constraints.Returns(new ProviderConstraints());
        dst.WriteMessageAsync(Arg.Any<FolderPath>(), Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>())
            .Returns(new WriteResult(true, "new-id"));
        return dst;
    }

    [Fact]
    public async Task Classifies_missing_as_copy_incomplete_as_backfill_complete_as_skip()
    {
        var src = Substitute.For<ISourceProvider>();
        src.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CanonicalFolder> { new(FolderPath.Parse("Inbox"), 3) });
        src.ReadMessagesAsync(Arg.Any<FolderPath>(), Arg.Any<ReadOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Messages(
                Msg("<a@x>", Att1),        // complete at dest → skip
                Msg("<b@x>", Att1, Att2),  // dest has only Att1 → backfill Att2
                Msg("<c@x>")));            // absent at dest → copy

        var dst = ReconcilableDest();
        var rec = (IReconcilableDestination)dst;
        rec.ScanFolderAsync(Arg.Any<FolderPath>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Digests(
                new DestMessageDigest("<a@x>", "destA", new[] { Att1 }),
                new DestMessageDigest("<b@x>", "destB", new[] { Att1 })));
        rec.BackfillAttachmentsAsync(Arg.Any<FolderPath>(), Arg.Any<string>(),
                Arg.Any<CanonicalMessage>(), Arg.Any<IReadOnlyList<CanonicalAttachmentInfo>>(), Arg.Any<CancellationToken>())
            .Returns(new BackfillResult(1, 0));

        var (sessions, _) = Sessions(src, dst);
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(Mid, Arg.Any<CancellationToken>()).Returns(new LedgerCounts(1, 1, 0, 0));
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(Arg.Any<RateLimitKey>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        var status = Substitute.For<IMigrationStatusWriter>();

        await using var provider = BuildHarness(sessions, Lookup(), ledger, limiter, status);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new ReconcileMailbox(Mid));
            (await harness.Consumed.Any<ReconcileMailbox>()).Should().BeTrue();

            // C (absent) → exactly one whole-message copy, and it is C.
            await dst.Received(1).WriteMessageAsync(Arg.Any<FolderPath>(), Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>());
            await dst.Received(1).WriteMessageAsync(
                Arg.Any<FolderPath>(), Arg.Is<CanonicalMessage>(m => m.MessageId == "<c@x>"), Arg.Any<CancellationToken>());

            // B (incomplete) → exactly one backfill, onto destB, with ONLY the missing Att2.
            await rec.Received(1).BackfillAttachmentsAsync(
                Arg.Any<FolderPath>(), "destB", Arg.Is<CanonicalMessage>(m => m.MessageId == "<b@x>"),
                Arg.Is<IReadOnlyList<CanonicalAttachmentInfo>>(l => l.Count == 1 && l[0].FileName == "a2.png"),
                Arg.Any<CancellationToken>());

            await status.Received(1).SetTerminalAsync(Mid, Arg.Any<LedgerCounts>(), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Emits_per_folder_progress_with_running_reconcile_totals()
    {
        var src = Substitute.For<ISourceProvider>();
        // Two folders → two per-folder progress publishes; FolderTotal must be 2 throughout.
        src.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CanonicalFolder>
            {
                new(FolderPath.Parse("Inbox"), 2),
                new(FolderPath.Parse("Sent"), 2),
            });
        // Each folder yields one complete (skip) + one absent (copy) message.
        src.ReadMessagesAsync(Arg.Any<FolderPath>(), Arg.Any<ReadOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Messages(Msg("<a@x>", Att1), Msg("<c@x>")));

        var dst = ReconcilableDest();
        var rec = (IReconcilableDestination)dst;
        rec.ScanFolderAsync(Arg.Any<FolderPath>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Digests(new DestMessageDigest("<a@x>", "destA", new[] { Att1 })));

        var (sessions, _) = Sessions(src, dst);
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(Mid, Arg.Any<CancellationToken>()).Returns(new LedgerCounts(2, 2, 0, 0));
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(Arg.Any<RateLimitKey>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        var status = Substitute.For<IMigrationStatusWriter>();

        await using var provider = BuildHarness(sessions, Lookup(), ledger, limiter, status);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new ReconcileMailbox(Mid));
            (await harness.Consumed.Any<ReconcileMailbox>()).Should().BeTrue();

            var progress = (await harness.Published.SelectAsync<MigrationProgressEvent>().ToListAsync())
                .Select(p => p.Context.Message)
                .Where(m => m.Reconcile is not null)
                .ToList();

            progress.Count.Should().BeGreaterThanOrEqualTo(2);
            progress.Should().OnlyContain(m => m.Reconcile!.FolderTotal == 2);
            progress.Should().OnlyContain(m => m.Status == "Running");

            // Running totals are non-decreasing across folders, and the last event reflects both folders.
            progress.Select(m => m.Reconcile!.Copied).Should().BeInAscendingOrder();
            progress.Select(m => m.Reconcile!.Skipped).Should().BeInAscendingOrder();
            progress.Select(m => m.Reconcile!.FoldersDone).Should().BeInAscendingOrder();

            var last = progress[^1];
            last.Reconcile!.FoldersDone.Should().Be(2);
            last.Reconcile.Copied.Should().Be(2);  // one absent message per folder
            last.Reconcile.Skipped.Should().Be(2);  // one complete message per folder
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Unsupported_destination_sets_not_supported_and_performs_no_writes()
    {
        var src = Substitute.For<ISourceProvider>();
        var dst = Substitute.For<IDestinationProvider>(); // NOT IReconcilableDestination
        dst.Id.Returns(new ProviderId("gmail"));

        var (sessions, _) = Sessions(src, dst);
        var ledger = Substitute.For<ILedger>();
        var limiter = Substitute.For<IRateLimiter>();
        var status = Substitute.For<IMigrationStatusWriter>();

        await using var provider = BuildHarness(sessions, Lookup(), ledger, limiter, status);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new ReconcileMailbox(Mid));
            (await harness.Consumed.Any<ReconcileMailbox>()).Should().BeTrue();

            await status.Received(1).SetNotSupportedAsync(Mid, Arg.Any<string>(), Arg.Any<CancellationToken>());
            await dst.DidNotReceive().WriteMessageAsync(Arg.Any<FolderPath>(), Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>());
            await src.DidNotReceive().ListFoldersAsync(Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Rerun_over_matched_destination_performs_zero_writes_and_backfills()
    {
        var src = Substitute.For<ISourceProvider>();
        src.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CanonicalFolder> { new(FolderPath.Parse("Inbox"), 3) });
        src.ReadMessagesAsync(Arg.Any<FolderPath>(), Arg.Any<ReadOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Messages(Msg("<a@x>", Att1), Msg("<b@x>", Att1, Att2), Msg("<c@x>")));

        var dst = ReconcilableDest();
        var rec = (IReconcilableDestination)dst;
        rec.ScanFolderAsync(Arg.Any<FolderPath>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Digests(
                new DestMessageDigest("<a@x>", "destA", new[] { Att1 }),
                new DestMessageDigest("<b@x>", "destB", new[] { Att1, Att2 }),
                new DestMessageDigest("<c@x>", "destC", System.Array.Empty<CanonicalAttachmentInfo>())));

        var (sessions, _) = Sessions(src, dst);
        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(Mid, Arg.Any<CancellationToken>()).Returns(new LedgerCounts(0, 3, 0, 0));
        var limiter = Substitute.For<IRateLimiter>();
        limiter.TryAcquireAsync(Arg.Any<RateLimitKey>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        var status = Substitute.For<IMigrationStatusWriter>();

        await using var provider = BuildHarness(sessions, Lookup(), ledger, limiter, status);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new ReconcileMailbox(Mid));
            (await harness.Consumed.Any<ReconcileMailbox>()).Should().BeTrue();

            await dst.DidNotReceive().WriteMessageAsync(Arg.Any<FolderPath>(), Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>());
            await rec.DidNotReceive().BackfillAttachmentsAsync(
                Arg.Any<FolderPath>(), Arg.Any<string>(), Arg.Any<CanonicalMessage>(),
                Arg.Any<IReadOnlyList<CanonicalAttachmentInfo>>(), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }
}
