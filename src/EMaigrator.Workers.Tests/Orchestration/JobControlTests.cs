using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Contracts;
using EMaigrator.Workers.Consumers;
using EMaigrator.Workers.Control;
using EMaigrator.Workers.Orchestration;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Orchestration;

public sealed class JobControlTests
{
    [Fact]
    public async Task Enqueue_publishes_single_start_migration()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orch = new MassTransitJobOrchestrator(harness.Bus);
            var mid = Guid.NewGuid();
            await orch.EnqueueMigrationAsync(mid, CancellationToken.None);
            var published = await harness.Published.SelectAsync<StartMigration>().ToListAsync();
            published.Should().ContainSingle(p => p.Context.Message.MailboxMigrationId == mid);
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Cancel_flips_gate_to_cancelled()
    {
        var gate = Substitute.For<IMigrationControlGate>();
        var lookup = Substitute.For<IJobMigrationLookup>();
        var jobId = Guid.NewGuid();

        await using var provider = new ServiceCollection()
            .AddSingleton(gate).AddSingleton(lookup)
            .AddMassTransitTestHarness(x => x.AddConsumer<JobControlConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orch = new MassTransitJobOrchestrator(harness.Bus);
            await orch.RequestCancelAsync(jobId, CancellationToken.None);
            (await harness.Consumed.Any<CancelJob>()).Should().BeTrue();
            await gate.Received().CancelAsync(jobId, Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    [Fact]
    public async Task Resume_reenqueues_start_for_each_not_done_migration()
    {
        var gate = Substitute.For<IMigrationControlGate>();
        var lookup = Substitute.For<IJobMigrationLookup>();
        var jobId = Guid.NewGuid();
        var m1 = Guid.NewGuid();
        var m2 = Guid.NewGuid();
        lookup.GetNotDoneMigrationsAsync(jobId, Arg.Any<CancellationToken>())
              .Returns(new List<Guid> { m1, m2 });

        await using var provider = new ServiceCollection()
            .AddSingleton(gate).AddSingleton(lookup)
            .AddMassTransitTestHarness(x => x.AddConsumer<JobControlConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orch = new MassTransitJobOrchestrator(harness.Bus);
            await orch.RequestResumeAsync(jobId, CancellationToken.None);
            (await harness.Consumed.Any<ResumeJob>()).Should().BeTrue();
            await gate.Received().ResumeAsync(jobId, Arg.Any<CancellationToken>());
            var starts = (await harness.Published.SelectAsync<StartMigration>().ToListAsync())
                .Select(p => p.Context.Message.MailboxMigrationId).ToList();
            starts.Should().BeEquivalentTo(new[] { m1, m2 });
        }
        finally { await harness.Stop(); }
    }
}
