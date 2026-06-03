using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Configuration;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Model;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Sessions;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Consumers;

public sealed class MigrateFolderConsumerTests
{
    private static readonly Guid Mid = Guid.NewGuid();
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid FolderTaskId = Guid.NewGuid();

    private static async IAsyncEnumerable<string> Refs(int n, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < n; i++) { ct.ThrowIfCancellationRequested(); yield return $"ref-{i}"; await Task.Yield(); }
    }

    private static ServiceProvider Build(MigrationControlState state, int refCount, out ITestHarness _)
    {
        var src = Substitute.For<ISourceProvider>();
        var sessions = Substitute.For<IProviderSessionFactory>();
        sessions.CreateSourceAsync(Mid, Arg.Any<CancellationToken>()).Returns(src);

        var lister = Substitute.For<IMessageRefLister>();
        lister.ListRefsAsync(src, Arg.Any<FolderPath>(), Arg.Any<CancellationToken>()).Returns(Refs(refCount));

        var gate = Substitute.For<IMigrationControlGate>();
        gate.GetStateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(state);

        var lookup = Substitute.For<IMigrationConnectionLookup>();
        lookup.GetAsync(Mid, Arg.Any<CancellationToken>()).Returns(new MigrationConnections(
            JobId, "t1",
            new ConnectionDescriptor { Provider = new("imap"), Auth = AuthMethod.ImapBasic, Settings = new Dictionary<string, string>() },
            new ConnectionDescriptor { Provider = new("graph"), Auth = AuthMethod.GraphAppOAuth, Settings = new Dictionary<string, string>() }));

        var provider = new ServiceCollection()
            .AddSingleton(sessions).AddSingleton(lister).AddSingleton(gate).AddSingleton(lookup)
            .AddSingleton<IOptions<OrchestrationOptions>>(Options.Create(new OrchestrationOptions { BatchSize = 100 }))
            .AddMassTransitTestHarness(x => x.AddConsumer<MigrateFolderConsumer>())
            .BuildServiceProvider(true);
        _ = provider.GetRequiredService<ITestHarness>();
        return provider;
    }

    [Fact]
    public async Task Pages_into_batches_of_batchsize()
    {
        await using var provider = Build(MigrationControlState.Active, 250, out _);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new MigrateFolder(Mid, FolderTaskId, "Inbox", "Inbox"));
            (await harness.Consumed.Any<MigrateFolder>()).Should().BeTrue();

            var batches = (await harness.Published.SelectAsync<MigrateBatch>().ToListAsync())
                .Select(p => p.Context.Message).ToList();
            batches.Should().HaveCount(3);
            batches.Select(b => b.SourceMessageRefs.Count).Should().BeEquivalentTo(new[] { 100, 100, 50 });
            batches.SelectMany(b => b.SourceMessageRefs).Distinct().Should().HaveCount(250);
            batches.Should().OnlyContain(b => b.MailboxMigrationId == Mid && b.FolderTaskId == FolderTaskId
                && b.SourceFolder == "Inbox" && b.DestFolder == "Inbox");
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Paused_job_publishes_no_batches()
    {
        await using var provider = Build(MigrationControlState.Paused, 250, out _);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            await harness.Bus.Publish(new MigrateFolder(Mid, FolderTaskId, "Inbox", "Inbox"));
            (await harness.Consumed.Any<MigrateFolder>()).Should().BeTrue();
            (await harness.Published.SelectAsync<MigrateBatch>().ToListAsync()).Should().BeEmpty();
        }
        finally { await harness.Stop(); }
    }
}
