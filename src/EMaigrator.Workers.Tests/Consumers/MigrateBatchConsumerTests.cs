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
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Copy;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Consumers;

public sealed class MigrateBatchConsumerTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid FolderTaskId = Guid.NewGuid();

    private static CanonicalMessage Msg(string key) => new()
    {
        IdentityKey = key,
        InternalDate = DateTimeOffset.UtcNow,
        SizeBytes = 3,
        OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 }))
    };

    private static (ServiceProvider provider, ILedger ledger, IRateLimiter limiter) Build(
        MigrationControlState state, bool throttleSecond)
    {
        var src = Substitute.For<ISourceProvider>();
        var dst = Substitute.For<IDestinationProvider>();
        dst.Id.Returns(new ProviderId("graph"));
        dst.WriteMessageAsync(Arg.Any<FolderPath>(), Arg.Any<CanonicalMessage>(), Arg.Any<CancellationToken>())
           .Returns(new WriteResult(true, "d"));

        var sessions = Substitute.For<IProviderSessionFactory>();
        sessions.CreateSourceAsync(Mid, Arg.Any<CancellationToken>()).Returns(src);
        sessions.CreateDestinationAsync(Mid, Arg.Any<CancellationToken>()).Returns(dst);

        var hydrator = Substitute.For<IMessageHydrator>();
        hydrator.HydrateAsync(src, Arg.Any<FolderPath>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => Msg((string)ci[2]));

        var ledger = Substitute.For<ILedger>();
        ledger.IsDoneAsync(Mid, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        ledger.GetCountsAsync(Mid, Arg.Any<CancellationToken>()).Returns(new LedgerCounts(2, 0, 0, 0));

        var limiter = Substitute.For<IRateLimiter>();
        if (throttleSecond)
        {
            var calls = 0;
            limiter.TryAcquireAsync(Arg.Any<RateLimitKey>(), 1, Arg.Any<CancellationToken>())
                   .Returns(_ => Task.FromResult(++calls <= 1)); // first ok, then throttled
        }
        else
        {
            limiter.TryAcquireAsync(Arg.Any<RateLimitKey>(), 1, Arg.Any<CancellationToken>()).Returns(true);
        }

        var gate = Substitute.For<IMigrationControlGate>();
        gate.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(state);

        var lookup = Substitute.For<IMigrationConnectionLookup>();
        lookup.GetAsync(Mid, Arg.Any<CancellationToken>()).Returns(new MigrationConnections(
            JobId, "t1",
            new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
            new ConnectionDescriptor { Provider = new("graph"), Auth = AuthMethod.GraphAppOAuth, Settings = new Dictionary<string, string> { ["accountEmail"] = "dest@biz.com" } }));

        var copierFactory = new StreamingCopierFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        var provider = new ServiceCollection()
            .AddSingleton(sessions).AddSingleton(hydrator).AddSingleton(gate).AddSingleton(lookup)
            .AddSingleton(ledger).AddSingleton(limiter).AddSingleton(copierFactory)
            .AddMassTransitTestHarness(x => x.AddConsumer<MigrateBatchConsumer>())
            .BuildServiceProvider(true);
        return (provider, ledger, limiter);
    }

    [Fact]
    public async Task Copies_all_and_publishes_progress()
    {
        var (provider, ledger, _) = Build(MigrationControlState.Active, throttleSecond: false);
        await using var _p = provider;
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new MigrateBatch(Mid, FolderTaskId, "Inbox", "Inbox", new[] { "ref-1", "ref-2" }));
            (await harness.Consumed.Any<MigrateBatch>()).Should().BeTrue();
            var progress = (await harness.Published.SelectAsync<MigrationProgressEvent>().ToListAsync())
                .Select(p => p.Context.Message).Single();
            progress.Migrated.Should().Be(2);
            progress.Status.Should().Be("Running");
            await ledger.Received(2).MarkAsync(
                Arg.Is(Mid), Arg.Any<string>(), Arg.Is("Inbox"), Arg.Is("Inbox"),
                Arg.Is(LedgerStatus.Migrated), Arg.Is<string?>(s => s == null), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Throttle_penalizes_and_faults_for_redelivery()
    {
        var (provider, _, limiter) = Build(MigrationControlState.Active, throttleSecond: true);
        await using var _p = provider;
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new MigrateBatch(Mid, FolderTaskId, "Inbox", "Inbox", new[] { "ref-1", "ref-2" }));
            var consumed = await harness.Consumed.SelectAsync<MigrateBatch>().FirstOrDefault();
            consumed.Should().NotBeNull();
            consumed!.Exception.Should().BeOfType<ThrottledRequeueException>();
            await limiter.Received().PenalizeAsync(Arg.Any<RateLimitKey>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Paused_at_start_copies_nothing()
    {
        var (provider, ledger, _) = Build(MigrationControlState.Paused, throttleSecond: false);
        await using var _p = provider;
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new MigrateBatch(Mid, FolderTaskId, "Inbox", "Inbox", new[] { "ref-1" }));
            (await harness.Consumed.Any<MigrateBatch>()).Should().BeTrue();
            await ledger.DidNotReceive().MarkAsync(Mid, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<LedgerStatus>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }
}
