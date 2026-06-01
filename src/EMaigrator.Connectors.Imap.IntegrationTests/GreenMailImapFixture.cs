using System.Text;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MimeKit;

namespace EMaigrator.Connectors.Imap.IntegrationTests;

/// <summary>
/// A real GreenMail IMAP+SMTP server in a container. Plaintext ports are used —
/// this is test-only and the connector is exercised with explicit allowPlaintext.
/// </summary>
public sealed class GreenMailImapFixture : IAsyncLifetime
{
    public const string UserEmail = "migrator@local.test";
    public const string UserName = "migrator";
    public const string Password = "pw";

    private const int ApiContainerPort = 8080;

    private IContainer _container = null!;
    public string Host { get; private set; } = "127.0.0.1";
    public int ImapPort { get; private set; }
    public int SmtpPort { get; private set; }
    public int ApiPort { get; private set; }

    public async Task InitializeAsync()
    {
        // NOTE: auth is ENFORCED (no -Dgreenmail.auth.disabled). This is deliberate:
        // the Task 11 security gate must be able to provoke a REAL authentication
        // failure (wrong password -> imap:auth-failed). With auth disabled GreenMail
        // accepts any credentials and the credential-leak-on-failure check could never
        // fire — exactly the "cheaper substitute check" the user-gate forbids.
        // -Dgreenmail.setup.test.all provisions all protocol listeners; SeedUserAsync
        // below creates migrator@local.test at the known password via the management API.
        _container = new ContainerBuilder("greenmail/standalone:2.1.0")
            .WithEnvironment("GREENMAIL_OPTS",
                "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.verbose")
            .WithPortBinding(3143, true)
            .WithPortBinding(3025, true)
            .WithPortBinding(ApiContainerPort, true)
            // The external-TCP-port wait alone races GreenMail's listener init (a first
            // protocol connect can be accepted then immediately closed). Wait for the final
            // startup log line ("Starting GreenMail API server ...") which is emitted only
            // after every mail listener — including imap:3143 — reports "Started".
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged(new Regex("Starting GreenMail API server")))
            .Build();
        await _container.StartAsync();
        ImapPort = _container.GetMappedPublicPort(3143);
        SmtpPort = _container.GetMappedPublicPort(3025);
        ApiPort = _container.GetMappedPublicPort(ApiContainerPort);
        await SeedUserAsync();
    }

    /// <summary>
    /// Provisions migrator@local.test at <see cref="Password"/> via GreenMail's
    /// management REST API (POST /api/user). With auth ENFORCED, GreenMail does not
    /// auto-create accounts on IMAP login, so the user must be created explicitly.
    /// After this, an IMAP login with the exact password succeeds and a login with any
    /// OTHER password fails deterministically — which the Task 11 security gate relies on.
    /// </summary>
    public async Task SeedUserAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://{Host}:{ApiPort}") };
        var payload = $$"""{"email":"{{UserEmail}}","login":"{{UserEmail}}","password":"{{Password}}"}""";
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(new Uri("/api/user", UriKind.Relative), content);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Deliver a message to the user via SMTP so it lands in INBOX.</summary>
    public async Task DeliverToInboxAsync(string subject, string body, string messageId)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@local.test"));
        msg.To.Add(new MailboxAddress("Migrator", UserEmail));
        msg.Subject = subject;
        msg.MessageId = messageId;
        msg.Body = new TextPart("plain") { Text = body };
        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(Host, SmtpPort, MailKit.Security.SecureSocketOptions.None);
        await smtp.SendAsync(msg);
        await smtp.DisconnectAsync(true);
    }

    /// <summary>APPEND directly into a (possibly new) folder, preserving flags+date.</summary>
    public async Task AppendAsync(string folderName, string subject, string body, string messageId,
        MailKit.MessageFlags flags, DateTimeOffset date)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(Host, ImapPort, MailKit.Security.SecureSocketOptions.None);
        await client.AuthenticateAsync(UserEmail, Password);
        var inbox = client.Inbox!;
        var folder = inbox;
        if (!folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            try { folder = (await inbox.GetSubfolderAsync(folderName))!; }
            catch (FolderNotFoundException) { folder = (await inbox.CreateAsync(folderName, true))!; }
        }
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@local.test"));
        msg.To.Add(new MailboxAddress("Migrator", UserEmail));
        msg.Subject = subject;
        msg.MessageId = messageId;
        msg.Body = new TextPart("plain") { Text = body };
        await folder.OpenAsync(FolderAccess.ReadWrite);
        await folder.AppendAsync(new AppendRequest(msg, flags, date));
        await client.DisconnectAsync(true);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("greenmail")]
public sealed class GreenMailCollectionMarker : ICollectionFixture<GreenMailImapFixture> { }
