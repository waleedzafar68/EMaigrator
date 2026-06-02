using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Contracts;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Workers.Consumers;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EMaigrator.Workers.Tests.Consumers;

public sealed class MigrateBatchFaultConsumerTests
{
    private static readonly Guid Mid = Guid.NewGuid();

    [Fact]
    public async Task Fault_records_content_free_needs_decision_and_marks_failed()
    {
        var ledger = Substitute.For<ILedger>();

        await using var provider = new ServiceCollection()
            .AddSingleton(ledger)
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<MigrateBatchFaultConsumer>();
                x.AddConsumer<CollectingNeedsDecisionConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var poison = new MigrateBatch(Mid, Guid.NewGuid(), "Inbox", "Inbox", new[] { "h:aaa", "h:bbb" });
            // Simulate MassTransit producing a Fault<MigrateBatch> after retries are exhausted.
            await harness.Bus.Publish<Fault<MigrateBatch>>(new
            {
                Message = poison,
                Exceptions = new[] { new ExceptionInfoStub() },
                FaultId = Guid.NewGuid(),
                FaultedMessageId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow
            });

            (await harness.Consumed.Any<Fault<MigrateBatch>>()).Should().BeTrue();

            var nd = (await harness.Published.SelectAsync<NeedsDecisionEvent>().ToListAsync())
                .Select(p => p.Context.Message).Single();
            nd.MailboxMigrationId.Should().Be(Mid);
            nd.IssueType.Should().Be("PoisonBatch");
            nd.Options.Should().BeEquivalentTo(new[] { RemediationAction.SkipMessage });
            nd.Detail.Should().Contain("h:aaa").And.Contain("h:bbb").And.Contain("Inbox");
            // Content-free: no body/subject markers leak into the event.
            nd.Detail.ToLowerInvariant().Should().NotContain("body").And.NotContain("subject:");

            await ledger.Received().MarkAsync(Mid, "h:aaa", "Inbox", "Inbox", LedgerStatus.Failed, Arg.Any<string>(), Arg.Any<CancellationToken>());
            await ledger.Received().MarkAsync(Mid, "h:bbb", "Inbox", "Inbox", LedgerStatus.Failed, Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally { await harness.Stop(); }
    }

    // Minimal stub so the dynamic Fault<> proxy has an ExceptionInfo with a usable type name.
    public sealed class ExceptionInfoStub : ExceptionInfo
    {
        public string ExceptionType => "EMaigrator.Workers.Tests.PoisonException";
        public ExceptionInfo? InnerException => null;
        public string StackTrace => "";
        public string Message => "message too large";
        public string Source => "test";
        public IDictionary<string, object> Data => new Dictionary<string, object>();
    }

    // Collector to keep the bus topology valid for the NeedsDecisionEvent publish.
    public sealed class CollectingNeedsDecisionConsumer : IConsumer<NeedsDecisionEvent>
    {
        public Task Consume(ConsumeContext<NeedsDecisionEvent> context) => Task.CompletedTask;
    }
}
