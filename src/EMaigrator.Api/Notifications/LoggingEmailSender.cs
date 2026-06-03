using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EMaigrator.Api.Notifications;

/// <summary>
/// Default self-host implementation: logs the email (subject + recipient only — no body, no credentials).
/// Hosted deployments swap this for an SMTP/provider-backed <see cref="IAppEmailSender"/> via DI.
/// </summary>
public sealed partial class LoggingEmailSender : IAppEmailSender
{
    private readonly ILogger<LoggingEmailSender> _log;

    public LoggingEmailSender(ILogger<LoggingEmailSender> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        MigrationEmail(_log, toEmail, subject);
        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Migration email to {To}: {Subject}")]
    private static partial void MigrationEmail(ILogger logger, string to, string subject);
}
