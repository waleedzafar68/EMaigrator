using System.Net;
using System.Text;
using EMaigrator.Core.Model;
using FluentAssertions;
using MimeKit;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphDestinationLargeWriteTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private const string FoldersListJson = """
        {
          "value": [
            { "id": "inbox-id", "displayName": "Inbox", "parentFolderId": "msgfolderroot", "totalItemCount": 0, "childFolderCount": 1 },
            { "id": "projects-id", "displayName": "Projects", "parentFolderId": "inbox-id", "totalItemCount": 0, "childFolderCount": 0 }
          ]
        }
        """;

    private const string CreatedDraftJson =
        """{ "id": "draft-id", "internetMessageId": "<m1@contoso.com>", "subject": "x" }""";

    private const string CreatedMessageJson =
        """{ "id": "imported-msg-id", "internetMessageId": "<m1@contoso.com>", "subject": "x" }""";

    private GraphDestinationProvider NewProvider()
        => new(GraphTestClientFactory.Create(_server.Url!), "user@contoso.com");

    private void StubFolders() =>
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(FoldersListJson));

    // Live MIME-import flow: top-level create (draft "draft-id") → move into the folder (post-move id
    // "imported-msg-id", which attachments then address) → isRead PATCH.
    private void StubWriteFlow()
    {
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/messages").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json").WithBody(CreatedDraftJson));
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/messages/draft-id/move").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json").WithBody(CreatedMessageJson));
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/messages/imported-msg-id").UsingMethod("PATCH"))
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(CreatedMessageJson));
    }

    private static CanonicalMessage MessageFrom(byte[] raw) => new()
    {
        IdentityKey = "mid:<m1@contoso.com>",
        MessageId = "<m1@contoso.com>",
        InternalDate = new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero),
        OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(raw, writable: false)),
    };

    private static byte[] LargeMultipart(int attachmentBytes)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("A", "a@x.com"));
        msg.To.Add(new MailboxAddress("B", "b@y.com"));
        msg.Subject = "big";
        var text = new TextPart("plain") { Text = "hello" };
        var att = new MimePart("application", "octet-stream")
        {
            Content = new MimeContent(new MemoryStream(new byte[attachmentBytes])),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "big.bin" },
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = "big.bin",
        };
        msg.Body = new Multipart("mixed") { text, att };
        using var ms = new MemoryStream();
        msg.WriteTo(ms);
        return ms.ToArray();
    }

    private int PostCount(string pathEndsWith) => _server.LogEntries.Count(e =>
        e.RequestMessage?.Method == "POST" &&
        (e.RequestMessage?.Path?.EndsWith(pathEndsWith, StringComparison.Ordinal) ?? false));

    [Fact]
    public async Task Small_message_uses_single_import_no_attachment_calls()
    {
        StubFolders();
        StubWriteFlow();

        var result = await NewProvider().WriteMessageAsync(
            FolderPath.Parse("Inbox/Projects"),
            MessageFrom(Encoding.ASCII.GetBytes("Message-ID: <m1@contoso.com>\r\n\r\nbody")),
            CancellationToken.None);

        result.Written.Should().BeTrue();
        result.DestMessageId.Should().Be("imported-msg-id");

        // A small message is exactly one (top-level) import + one move; no attachment calls.
        PostCount("/user@contoso.com/messages").Should().Be(1);
        PostCount("/messages/draft-id/move").Should().Be(1);
        PostCount("/createUploadSession").Should().Be(0);
        _server.LogEntries.Count(e => e.RequestMessage?.Method == "POST").Should().Be(2);
    }

    [Fact]
    public async Task Large_message_strips_oversized_part_and_uploads_via_session()
    {
        StubFolders();
        StubWriteFlow();

        var uploadUrl = _server.Url! + "/uploadsession/abc";
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/messages/imported-msg-id/attachments/createUploadSession").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody($"{{\"uploadUrl\":\"{uploadUrl}\",\"expirationDateTime\":\"2030-01-01T00:00:00Z\",\"nextExpectedRanges\":[\"0-\"]}}"));
        _server.Given(Request.Create().WithPath("/uploadsession/abc").UsingPut())
               .RespondWith(Response.Create().WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json").WithBody("{\"id\":\"att-large\"}"));

        // 4 MB attachment → MIME > 4 MB base64 → hybrid path; stripped part (4 MB ≥ 3 MB) → upload session.
        var result = await NewProvider().WriteMessageAsync(
            FolderPath.Parse("Inbox/Projects"), MessageFrom(LargeMultipart(4 * 1024 * 1024)), CancellationToken.None);

        result.Written.Should().BeTrue();
        result.DestMessageId.Should().Be("imported-msg-id");

        PostCount("/user@contoso.com/messages").Should().Be(1);          // exactly one (reduced) import
        PostCount("/messages/draft-id/move").Should().Be(1);             // moved into the destination folder
        PostCount("/createUploadSession").Should().Be(1);                // the stripped part, re-uploaded
        _server.LogEntries.Count(e =>
            e.RequestMessage?.Method == "PUT" &&
            (e.RequestMessage?.Path?.EndsWith("/uploadsession/abc", StringComparison.Ordinal) ?? false))
            .Should().Be(1);
    }

    [Fact]
    public async Task Signed_message_is_not_stripped()
    {
        StubFolders();
        StubWriteFlow();

        // Top-level application/pkcs7-mime, > 4 MB base64 → hybrid path but S/MIME guard → no stripping.
        var blob = new string('A', 4_000_000); // ~4 MB raw → base64(MIME) > 4 MB
        var raw = "From: a@x.com\r\nSubject: s\r\nMIME-Version: 1.0\r\n" +
                  "Content-Type: application/pkcs7-mime; smime-type=enveloped-data; name=\"smime.p7m\"\r\n" +
                  "Content-Transfer-Encoding: base64\r\n\r\n" + blob + "\r\n";

        var result = await NewProvider().WriteMessageAsync(
            FolderPath.Parse("Inbox/Projects"), MessageFrom(Encoding.ASCII.GetBytes(raw)), CancellationToken.None);

        result.Written.Should().BeTrue();
        PostCount("/user@contoso.com/messages").Should().Be(1);          // single whole-MIME import attempt
        PostCount("/messages/draft-id/move").Should().Be(1);             // moved into the destination folder
        PostCount("/createUploadSession").Should().Be(0);                // never stripped
        _server.LogEntries.Count(e =>
            e.RequestMessage?.Method == "POST" &&
            (e.RequestMessage?.Path?.EndsWith("/attachments", StringComparison.Ordinal) ?? false))
            .Should().Be(0);
    }

    public void Dispose()
    {
        _server.Dispose();
        GC.SuppressFinalize(this);
    }
}
