using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
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

public sealed class StartMigrationSeedingTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    private static async IAsyncEnumerable<string> Refs(params string[] refs)
    {
        foreach (var r in refs)
        {
            yield return r;
        }

        await Task.CompletedTask;
    }

    private static MigrationConnections Conns() => new(
        JobId, "t1",
        new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
        new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() });

    private sealed record Built(
        ServiceProvider Provider, ITestHarness Harness, ILedger Ledger, IMigrationStatusWriter Status);

    private static Built Build(
        IReadOnlyList<CanonicalFolder> folders, Func<FolderPath, string[]> refsForFolder, LedgerCounts countsOnRead)
    {
        var src = Substitute.For<ISourceProvider>();
        src.ListFoldersAsync(Arg.Any<CancellationToken>()).Returns(folders);
        var dst = Substitute.For<IDestinationProvider>();
        dst.Constraints.Returns(new ProviderConstraints { MaxFolderDepth = 10 });

        var sessions = Substitute.For<IProviderSessionFactory>();
        sessions.CreateSourceAsync(Mid, Arg.Any<CancellationToken>()).Returns(src);
        sessions.CreateDestinationAsync(Mid, Arg.Any<CancellationToken>()).Returns(dst);

        var plan = Substitute.For<IRemediationPlanStore>();
        plan.GetApprovedAsync(Mid, Arg.Any<CancellationToken>()).Returns(new List<ApprovedRemediation>());

        var gate = Substitute.For<IMigrationControlGate>();
        gate.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(MigrationControlState.Active);

        var lookup = Substitute.For<IMigrationConnectionLookup>();
        lookup.GetAsync(Mid, Arg.Any<CancellationToken>()).Returns(Conns());

        var lister = Substitute.For<IMessageRefLister>();
        lister.ListRefsAsync(Arg.Any<ISourceProvider>(), Arg.Any<FolderPath>(), Arg.Any<CancellationToken>())
            .Returns(ci => Refs(refsForFolder(ci.ArgAt<FolderPath>(1))));

        var ledger = Substitute.For<ILedger>();
        ledger.GetCountsAsync(Mid, Arg.Any<CancellationToken>()).Returns(countsOnRead);

        var status = Substitute.For<IMigrationStatusWriter>();

        var provider = new ServiceCollection()
            .AddSingleton(sessions).AddSingleton(plan).AddSingleton(gate).AddSingleton(lookup)
            .AddSingleton(lister).AddSingleton(ledger).AddSingleton(status)
            .AddMassTransitTestHarness(x => x.AddConsumer<StartMigrationConsumer>())
            .BuildServiceProvider(true);

        return new Built(provider, provider.GetRequiredService<ITestHarness>(), ledger, status);
    }

    [Fact]
    public async Task Seeds_pending_for_every_folder_and_publishes_a_folder_message_each()
    {
        var folders = new List<CanonicalFolder>
        {
            new(FolderPath.Parse("Inbox"), 2),
            new(FolderPath.Parse("Archive"), 1),
        };
        var seeded = new List<string>();

        var b = Build(folders, f => [f + "-r0"], new LedgerCounts(0, 0, 0, 2));
        b.Ledger.WhenForAnyArgs(l => l.SeedPendingAsync(default, default!, default))
            .Do(ci => seeded.AddRange(
                ci.ArgAt<IEnumerable<(string IdentityKey, string SourceFolder, string DestFolder)>>(1)
                  .Select(t => t.IdentityKey)));

        await using var sp = b.Provider;
        await b.Harness.Start();
        try
        {
            await b.Harness.Bus.Publish(new StartMigration(Mid));
            (await b.Harness.Consumed.Any<StartMigration>()).Should().BeTrue();

            var published = await b.Harness.Published.SelectAsync<MigrateFolder>().ToListAsync();
            published.Should().HaveCount(2);
            seeded.Should().BeEquivalentTo("Inbox-r0", "Archive-r0");
            await b.Status.Received(1).SetRunningAsync(Mid, Arg.Any<CancellationToken>());
            await b.Status.DidNotReceiveWithAnyArgs().SetTerminalAsync(default, default!, default);
        }
        finally
        {
            await b.Harness.Stop();
        }
    }

    [Fact]
    public async Task Empty_mailbox_writes_terminal_completed_and_publishes_no_folder()
    {
        var b = Build([], _ => [], new LedgerCounts(0, 0, 0, 0));

        await using var sp = b.Provider;
        await b.Harness.Start();
        try
        {
            await b.Harness.Bus.Publish(new StartMigration(Mid));
            (await b.Harness.Consumed.Any<StartMigration>()).Should().BeTrue();

            (await b.Harness.Published.SelectAsync<MigrateFolder>().ToListAsync()).Should().BeEmpty();
            await b.Status.Received(1).SetRunningAsync(Mid, Arg.Any<CancellationToken>());
            await b.Status.Received(1).SetTerminalAsync(Mid,
                Arg.Is<LedgerCounts>(c => c.Pending == 0), Arg.Any<CancellationToken>());
        }
        finally
        {
            await b.Harness.Stop();
        }
    }
}
