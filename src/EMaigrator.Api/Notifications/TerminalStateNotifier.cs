using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EMaigrator.Core.Contracts;
using MassTransit;

namespace EMaigrator.Api.Notifications;

/// <summary>
/// MassTransit consumer that fires exactly one notification email per migration when it reaches a
/// terminal state (Completed/Partial/Failed/Cancelled). Non-terminal (Running) events are ignored.
/// Idempotency is enforced by <see cref="ISentGuard"/> (a sent-flag row), so duplicate terminal events
/// — or concurrent API instances — never resend.
/// </summary>
public sealed class TerminalStateNotifier : IConsumer<MigrationProgressEvent>
{
    private static readonly HashSet<string> Terminal = new(StringComparer.Ordinal)
    {
        "Completed", "Partial", "Failed", "Cancelled",
    };

    private readonly IAppEmailSender _email;
    private readonly INotificationRecipientResolver _resolver;
    private readonly ISentGuard _guard;

    public TerminalStateNotifier(IAppEmailSender email, INotificationRecipientResolver resolver, ISentGuard guard)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(guard);
        (_email, _resolver, _guard) = (email, resolver, guard);
    }

    public async Task Consume(ConsumeContext<MigrationProgressEvent> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var m = context.Message;
        if (!Terminal.Contains(m.Status))
        {
            return;
        }

        if (!await _guard.TryMarkSentAsync(m.MailboxMigrationId, context.CancellationToken).ConfigureAwait(false))
        {
            return; // already sent
        }

        var recipient = await _resolver.ResolveAsync(m.MailboxMigrationId, context.CancellationToken).ConfigureAwait(false);
        if (recipient is null)
        {
            return;
        }

        var (subject, body) = EmailTemplates.Render(m.Status, recipient.From, recipient.To, m.Migrated, 0, m.Total - m.Migrated);
        await _email.SendAsync(recipient.ToEmail, subject, body, context.CancellationToken).ConfigureAwait(false);
    }
}
