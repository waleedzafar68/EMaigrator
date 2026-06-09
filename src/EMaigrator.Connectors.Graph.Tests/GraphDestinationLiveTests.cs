using EMaigrator.Core.Model;
using FluentAssertions;
using MimeKit;
using Xunit.Abstractions;

namespace EMaigrator.Connectors.Graph.Tests;

/// <summary>
/// Gated LIVE end-to-end test of the Graph destination write path against a real Exchange Online mailbox.
/// This is the test that the WireMock suite cannot be: it actually validates that a MIME message is
/// accepted by Graph and lands in the destination folder (the folder-scoped MIME-import 400 that shipped
/// green under WireMock would fail HERE). Runs ONLY when all EMAIGRATOR_GRAPH_* env vars are set; the
/// default/CI run skips it. Creates a throwaway folder and deletes it (removing its messages) on the way out.
/// </summary>
public class GraphDestinationLiveTests
{
    private const string ReadSubject = "EMaigrator live — read message";
    private const string UnreadSubject = "EMaigrator live — unread message";

    private readonly ITestOutputHelper _out;

    public GraphDestinationLiveTests(ITestOutputHelper output) => _out = output;

    [SkippableFact]
    public async Task WriteMessage_lands_in_target_folder_with_preserved_read_state()
    {
        var tenant = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_CLIENT_SECRET");
        var account = Environment.GetEnvironmentVariable("EMAIGRATOR_GRAPH_ACCOUNT_EMAIL");
        Skip.If(
            string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(account),
            "Set EMAIGRATOR_GRAPH_* env vars to run the live destination test.");

        var ct = CancellationToken.None;
        var client = GraphClientFactory.Build(new GraphConnectionConfig
        {
            TenantId = tenant!, ClientId = clientId!, ClientSecret = secret!, AccountEmail = account!,
        });
        var provider = new GraphDestinationProvider(client, account!);

        var folderName = "EMaigratorLiveTest-" + Guid.NewGuid().ToString("N")[..8];
        var folder = FolderPath.Parse(folderName);

        await provider.EnsureFolderAsync(folder, ct);

        string? landedFolderId = null;
        try
        {
            var readWrite = await provider.WriteMessageAsync(
                folder, BuildMessage(account!, ReadSubject, MessageFlags.Seen), ct);
            var unreadWrite = await provider.WriteMessageAsync(
                folder, BuildMessage(account!, UnreadSubject, MessageFlags.None), ct);

            _out.WriteLine($"readWrite:   Written={readWrite.Written} id={readWrite.DestMessageId} error={readWrite.ErrorCode}");
            _out.WriteLine($"unreadWrite: Written={unreadWrite.Written} id={unreadWrite.DestMessageId} error={unreadWrite.ErrorCode}");

            readWrite.Written.Should().BeTrue($"write should succeed (error={readWrite.ErrorCode})");
            readWrite.DestMessageId.Should().NotBeNullOrEmpty();
            unreadWrite.Written.Should().BeTrue($"write should succeed (error={unreadWrite.ErrorCode})");
            unreadWrite.DestMessageId.Should().NotBeNullOrEmpty();

            // Verify via each message's OWN parentFolderId (robust to large/ paged folder lists), then
            // confirm that folder is in fact the one we created — proving the import + move actually landed
            // the MIME in the destination folder rather than leaving it as a draft in Drafts.
            var read = await client.Users[account].Messages[readWrite.DestMessageId]
                .GetAsync(rc => rc.QueryParameters.Select = ["id", "subject", "isRead", "parentFolderId"], ct);
            var unread = await client.Users[account].Messages[unreadWrite.DestMessageId]
                .GetAsync(rc => rc.QueryParameters.Select = ["id", "subject", "isRead", "parentFolderId"], ct);
            landedFolderId = read?.ParentFolderId;

            _out.WriteLine($"read  : subject='{read?.Subject}' isRead={read?.IsRead} parent={read?.ParentFolderId}");
            _out.WriteLine($"unread: subject='{unread?.Subject}' isRead={unread?.IsRead} parent={unread?.ParentFolderId}");

            var landedFolder = await client.Users[account].MailFolders[landedFolderId]
                .GetAsync(rc => rc.QueryParameters.Select = ["id", "displayName"], ct);
            landedFolder!.DisplayName.Should().Be(folderName,
                "the imported message must be moved into the destination folder we created, not left in Drafts");

            read!.Subject.Should().Be(ReadSubject);
            read.IsRead.Should().BeTrue("source flag Seen must be preserved as isRead=true");

            unread!.Subject.Should().Be(UnreadSubject);
            unread.ParentFolderId.Should().Be(landedFolderId, "both messages were written to the same folder");
            unread.IsRead.Should().BeFalse("a source message without Seen must be isRead=false");
        }
        finally
        {
            // Deleting the folder removes any messages we created inside it.
            try { await client.Users[account].MailFolders[landedFolderId].DeleteAsync(cancellationToken: ct); }
            catch { /* best effort cleanup */ }
        }
    }

    private static CanonicalMessage BuildMessage(string account, string subject, MessageFlags flags)
    {
        var mk = new MimeMessage();
        mk.From.Add(new MailboxAddress("EMaigrator", account));
        mk.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        mk.Subject = subject;
        mk.Date = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        mk.MessageId = Guid.NewGuid().ToString("N") + "@emaigrator.local";
        mk.Body = new TextPart("plain") { Text = "Live destination write-path test body." };
        using var ms = new MemoryStream();
        mk.WriteTo(ms);
        var bytes = ms.ToArray();

        return new CanonicalMessage
        {
            IdentityKey = "mid:" + mk.MessageId,
            MessageId = "<" + mk.MessageId + ">",
            InternalDate = mk.Date,
            Flags = flags,
            OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)),
        };
    }
}
