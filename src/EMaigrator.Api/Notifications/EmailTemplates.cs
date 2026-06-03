namespace EMaigrator.Api.Notifications;

/// <summary>
/// Renders the terminal-state notification email. The body carries only aggregate counts and endpoint
/// labels — never credentials or other secrets (asserted by the security verification).
/// </summary>
public static class EmailTemplates
{
    public static (string Subject, string HtmlBody) Render(string status, string from, string to,
        long migrated, long skipped, long failed)
    {
        var (subject, headline) = status switch
        {
            "Completed" => ($"Your {from} → {to} migration is complete", "Migration complete"),
            "Partial" => ($"Your {from} → {to} migration needs your decision", "Migration finished — some items need your decision"),
            "Failed" => ($"Your {from} → {to} migration failed", "Migration failed"),
            "Cancelled" => ($"Your {from} → {to} migration was cancelled", "Migration cancelled"),
            _ => ($"Your {from} → {to} migration update", "Migration update"),
        };
        var body =
            $"<h2>{headline}</h2>" +
            $"<p>Moving mail from <strong>{from}</strong> to <strong>{to}</strong>.</p>" +
            $"<ul><li>{migrated} migrated</li><li>{skipped} skipped</li><li>{failed} failed</li></ul>" +
            "<p>Sign in to EMaigrator to view the full results and audit log.</p>";
        return (subject, body);
    }
}
