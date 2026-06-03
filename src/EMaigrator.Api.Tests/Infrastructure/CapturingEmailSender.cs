using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EMaigrator.Api.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EMaigrator.Api.Tests.Infrastructure;

/// <summary>
/// Capturing <see cref="IAppEmailSender"/> for the functional capstone (Task 14): records every email the
/// <see cref="TerminalStateNotifier"/> sends so the end-to-end test can assert exactly one terminal-state
/// notification went out. Registered as a <b>singleton</b> replacing the production
/// <c>LoggingEmailSender</c> so the test reads the same instance from the root provider
/// (<c>_factory.Services</c>).
/// </summary>
public sealed class CapturingEmailSender : IAppEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = new();

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        Sent.Add((toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Deterministic <see cref="INotificationRecipientResolver"/> for the functional capstone: returns a fixed
/// recipient + endpoint labels for any mailbox-migration id, so the email path resolves without depending on
/// a seeded owning user. The production <see cref="DbNotificationRecipientResolver"/> is exercised by the
/// Task 11 unit suite; this stub only feeds the in-test <see cref="TerminalStateNotifier"/>.
/// </summary>
public sealed class StubRecipientResolver : INotificationRecipientResolver
{
    public Task<NotificationContext?> ResolveAsync(Guid mailboxMigrationId, CancellationToken ct) =>
        Task.FromResult<NotificationContext?>(new NotificationContext("owner@biz.com", "WorkMail", "Microsoft 365"));
}

/// <summary>
/// Wires the capturing email sender + stub recipient resolver into the test host.
/// <see cref="WithCapturingEmail"/> is a call-site marker; <see cref="ApiTestFactory"/> ALWAYS calls
/// <see cref="AddCapturingEmail"/>.
/// </summary>
public static class CapturingEmailExtensions
{
    public static ApiTestFactory WithCapturingEmail(this ApiTestFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory;
    }

    /// <summary>
    /// REMOVE the production <see cref="IAppEmailSender"/> (<c>LoggingEmailSender</c>, singleton) and the
    /// scoped <see cref="INotificationRecipientResolver"/> (<c>DbNotificationRecipientResolver</c>) then
    /// register the capturing sender + stub resolver as singletons so the functional test reads them from
    /// the root provider.
    /// </summary>
    public static void AddCapturingEmail(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<IAppEmailSender>();
        services.AddSingleton<IAppEmailSender, CapturingEmailSender>();
        services.RemoveAll<INotificationRecipientResolver>();
        services.AddSingleton<INotificationRecipientResolver, StubRecipientResolver>();
    }
}
