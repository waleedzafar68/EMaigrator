using System;
using System.Threading;
using System.Threading.Tasks;

namespace EMaigrator.Api.Notifications;

/// <summary>
/// Sends a terminal-state migration notification email. The OSS default
/// (<see cref="LoggingEmailSender"/>) logs the message; hosted deployments swap in an
/// SMTP/provider-backed implementation via DI.
/// </summary>
public interface IAppEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct);
}

/// <summary>Resolved recipient + endpoint labels for a terminal-state notification.</summary>
public sealed record NotificationContext(string ToEmail, string From, string To);

/// <summary>
/// Resolves the owning user's email + endpoint display labels from the mailbox-migration id.
/// </summary>
public interface INotificationRecipientResolver
{
    Task<NotificationContext?> ResolveAsync(Guid mailboxMigrationId, CancellationToken ct);
}

/// <summary>
/// Idempotency gate. <c>true</c> = this caller is the first to claim the migration (so it must send);
/// <c>false</c> = already claimed (so it must NOT send).
/// </summary>
public interface ISentGuard
{
    Task<bool> TryMarkSentAsync(Guid migrationId, CancellationToken ct);
}
