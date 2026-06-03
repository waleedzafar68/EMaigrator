using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Notifications;
using EMaigrator.Core.Contracts;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using Xunit;

namespace EMaigrator.Api.Tests;

public class TerminalStateNotifierTests
{
    [Fact]
    public void Template_reflects_terminal_state_and_endpoints()
    {
        var (subject, body) = EmailTemplates.Render("Completed", "WorkMail", "Microsoft 365", 3180, 18, 3);
        subject.ToLowerInvariant().Should().Contain("complete");
        body.Should().Contain("WorkMail").And.Contain("Microsoft 365");
    }

    [Fact]
    public void Partial_template_says_needs_decision()
    {
        var (subject, _) = EmailTemplates.Render("Partial", "WorkMail", "Google", 10, 0, 2);
        subject.ToLowerInvariant().Should().Contain("decision");
    }

    [Fact]
    public async Task Running_status_does_not_send()
    {
        var email = Substitute.For<IAppEmailSender>();
        var resolver = Substitute.For<INotificationRecipientResolver>();
        var notifier = new TerminalStateNotifier(email, resolver, new InMemorySentGuard());
        var ctx = Substitute.For<ConsumeContext<MigrationProgressEvent>>();
        ctx.Message.Returns(new MigrationProgressEvent(Guid.NewGuid(), 1, 10, "/Inbox", 1, "Running"));
        await notifier.Consume(ctx);
        await email.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Terminal_status_sends_once_only()
    {
        var email = Substitute.For<IAppEmailSender>();
        var resolver = Substitute.For<INotificationRecipientResolver>();
        var mbxId = Guid.NewGuid();
        resolver.ResolveAsync(mbxId, Arg.Any<CancellationToken>())
            .Returns(new NotificationContext("owner@biz.com", "WorkMail", "Microsoft 365"));
        var guard = new InMemorySentGuard();
        var notifier = new TerminalStateNotifier(email, resolver, guard);

        var ctx = Substitute.For<ConsumeContext<MigrationProgressEvent>>();
        ctx.Message.Returns(new MigrationProgressEvent(mbxId, 3180, 3201, null, 0, "Completed"));
        await notifier.Consume(ctx);
        await notifier.Consume(ctx);   // duplicate event

        await email.Received(1).SendAsync("owner@biz.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

// Test-only in-memory sent guard.
file sealed class InMemorySentGuard : ISentGuard
{
    private readonly HashSet<Guid> _sent = new();
    public Task<bool> TryMarkSentAsync(Guid migrationId, CancellationToken ct) => Task.FromResult(_sent.Add(migrationId));
}
