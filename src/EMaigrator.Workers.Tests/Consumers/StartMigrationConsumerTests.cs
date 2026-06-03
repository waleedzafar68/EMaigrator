using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Diagnostics;
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

public sealed class StartMigrationConsumerTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();

    private static (ISourceProvider src, IDestinationProvider dst) Providers()
    {
        var src = Substitute.For<ISourceProvider>();
        src.ListFoldersAsync(Arg.Any<CancellationToken>()).Returns(new List<CanonicalFolder>
        {
            new(FolderPath.Parse("Inbox"), 10),
            new(FolderPath.Parse("A/B/C/D/E"), 5)
        });
        var dst = Substitute.For<IDestinationProvider>();
        dst.Constraints.Returns(new ProviderConstraints { MaxFolderDepth = 3 });
        return (src, dst);
    }

    // A lister that yields one ref per folder, so seeding produces work (totalSeeded > 0) and the
    // consumer takes the normal fan-out path rather than the empty-mailbox terminal path.
    private static async IAsyncEnumerable<string> OneRef()
    {
        yield return "r1";
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Publishes_one_MigrateFolder_per_folder_with_flatten_applied()
    {
        var (src, dst) = Providers();
        var sessions = Substitute.For<IProviderSessionFactory>();
        sessions.CreateSourceAsync(Mid, Arg.Any<CancellationToken>()).Returns(src);
        sessions.CreateDestinationAsync(Mid, Arg.Any<CancellationToken>()).Returns(dst);

        var plan = Substitute.For<IRemediationPlanStore>();
        plan.GetApprovedAsync(Mid, Arg.Any<CancellationToken>()).Returns(new List<ApprovedRemediation>
        {
            new("A/B/C/D/E", RemediationAction.FlattenFolder)
        });

        var gate = Substitute.For<IMigrationControlGate>();
        gate.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(MigrationControlState.Active);

        var lookup = Substitute.For<IMigrationConnectionLookup>();
        lookup.GetAsync(Mid, Arg.Any<CancellationToken>()).Returns(new MigrationConnections(
            JobId, "t1",
            new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
            new ConnectionDescriptor { Provider = new("graph"), Auth = AuthMethod.GraphAppOAuth, Settings = new Dictionary<string, string>() }));

        var lister = Substitute.For<IMessageRefLister>();
        lister.ListRefsAsync(Arg.Any<ISourceProvider>(), Arg.Any<FolderPath>(), Arg.Any<CancellationToken>())
            .Returns(_ => OneRef());
        var ledger = Substitute.For<ILedger>();
        var status = Substitute.For<IMigrationStatusWriter>();

        await using var provider = new ServiceCollection()
            .AddSingleton(sessions).AddSingleton(plan).AddSingleton(gate).AddSingleton(lookup)
            .AddSingleton(lister).AddSingleton(ledger).AddSingleton(status)
            .AddMassTransitTestHarness(x => x.AddConsumer<StartMigrationConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new StartMigration(Mid));
            (await harness.Consumed.Any<StartMigration>()).Should().BeTrue();

            var published = await harness.Published.SelectAsync<MigrateFolder>().ToListAsync();
            published.Should().HaveCount(2);
            var folders = published.Select(p => p.Context.Message.DestFolder).ToList();
            folders.Should().Contain("Inbox");
            folders.Should().Contain(FolderFlattener.Flatten(FolderPath.Parse("A/B/C/D/E"), 3).ToString());
            await dst.Received(2).EnsureFolderAsync(Arg.Any<FolderPath>(), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Cancelled_job_publishes_nothing()
    {
        var (src, dst) = Providers();
        var sessions = Substitute.For<IProviderSessionFactory>();
        sessions.CreateSourceAsync(Mid, Arg.Any<CancellationToken>()).Returns(src);
        sessions.CreateDestinationAsync(Mid, Arg.Any<CancellationToken>()).Returns(dst);
        var plan = Substitute.For<IRemediationPlanStore>();
        plan.GetApprovedAsync(Mid, Arg.Any<CancellationToken>()).Returns(new List<ApprovedRemediation>());
        var gate = Substitute.For<IMigrationControlGate>();
        gate.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(MigrationControlState.Cancelled);
        var lookup = Substitute.For<IMigrationConnectionLookup>();
        lookup.GetAsync(Mid, Arg.Any<CancellationToken>()).Returns(new MigrationConnections(
            JobId, "t1",
            new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
            new ConnectionDescriptor { Provider = new("graph"), Auth = AuthMethod.GraphAppOAuth, Settings = new Dictionary<string, string>() }));

        // Registered (the consumer ctor requires them) but never exercised: the cancel guard returns
        // before any seeding / status write happens.
        var lister = Substitute.For<IMessageRefLister>();
        var ledger = Substitute.For<ILedger>();
        var status = Substitute.For<IMigrationStatusWriter>();

        await using var provider = new ServiceCollection()
            .AddSingleton(sessions).AddSingleton(plan).AddSingleton(gate).AddSingleton(lookup)
            .AddSingleton(lister).AddSingleton(ledger).AddSingleton(status)
            .AddMassTransitTestHarness(x => x.AddConsumer<StartMigrationConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new StartMigration(Mid));
            (await harness.Consumed.Any<StartMigration>()).Should().BeTrue();
            (await harness.Published.SelectAsync<MigrateFolder>().ToListAsync()).Should().BeEmpty();
            await status.DidNotReceiveWithAnyArgs().SetRunningAsync(default, default);
        }
        finally { await harness.Stop(); }
    }
}
