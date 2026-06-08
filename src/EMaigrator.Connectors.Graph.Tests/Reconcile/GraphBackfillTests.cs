using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using MimeKit;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EMaigrator.Connectors.Graph.Tests.Reconcile;

public class GraphBackfillTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private GraphDestinationProvider NewProvider()
        => new(GraphTestClientFactory.Create(_server.Url!), "user@contoso.com");

    private void StubAttachmentPost() =>
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/messages/DEST/attachments").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json").WithBody("{\"id\":\"att\"}"));

    private static CanonicalMessage SourceWith(params (string Name, string Type)[] parts)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("A", "a@x.com"));
        msg.Subject = "s";
        var multipart = new Multipart("mixed") { new TextPart("plain") { Text = "body" } };
        foreach (var (name, type) in parts)
        {
            var slash = type.IndexOf('/', StringComparison.Ordinal);
            var att = new MimePart(type[..slash], type[(slash + 1)..])
            {
                Content = new MimeContent(new MemoryStream(new byte[16])),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = name },
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = name,
            };
            multipart.Add(att);
        }

        msg.Body = multipart;
        using var ms = new MemoryStream();
        msg.WriteTo(ms);
        var raw = ms.ToArray();
        return new CanonicalMessage
        {
            IdentityKey = "mid:<s@x.com>",
            InternalDate = DateTimeOffset.UnixEpoch,
            OpenContentAsync = _ => Task.FromResult<Stream>(new MemoryStream(raw, writable: false)),
        };
    }

    private int AttachmentPostCount() => _server.LogEntries.Count(e =>
        e.RequestMessage?.Method == "POST" &&
        (e.RequestMessage?.Path?.EndsWith("/messages/DEST/attachments", StringComparison.Ordinal) ?? false));

    [Fact]
    public async Task Backfills_only_the_missing_parts()
    {
        StubAttachmentPost();
        var source = SourceWith(("a.pdf", "application/pdf"), ("b.png", "image/png"), ("c.txt", "text/plain"));
        var missing = new[]
        {
            new CanonicalAttachmentInfo("a.pdf", "application/pdf", 16),
            new CanonicalAttachmentInfo("b.png", "image/png", 16),
        };

        var result = await NewProvider().BackfillAttachmentsAsync(
            FolderPath.Parse("Inbox"), "DEST", source, missing, CancellationToken.None);

        result.Added.Should().Be(2);
        result.Failed.Should().Be(0);
        AttachmentPostCount().Should().Be(2); // c.txt (present, not requested) is NOT re-uploaded
    }

    [Fact]
    public async Task Missing_part_absent_from_source_is_recorded_as_failed()
    {
        StubAttachmentPost();
        var source = SourceWith(("a.pdf", "application/pdf"));
        var missing = new[]
        {
            new CanonicalAttachmentInfo("a.pdf", "application/pdf", 16),
            new CanonicalAttachmentInfo("z.zip", "application/zip", 16),
        };

        var result = await NewProvider().BackfillAttachmentsAsync(
            FolderPath.Parse("Inbox"), "DEST", source, missing, CancellationToken.None);

        result.Added.Should().Be(1);
        result.Failed.Should().Be(1);
        result.ErrorCode.Should().Be("attachment-not-found-in-source");
        AttachmentPostCount().Should().Be(1);
    }

    [Fact]
    public async Task Upload_failure_increments_failed_with_normalized_code()
    {
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/messages/DEST/attachments").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(400)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("{\"error\":{\"code\":\"ErrorInvalid\",\"message\":\"bad\"}}"));

        var source = SourceWith(("a.pdf", "application/pdf"));
        var missing = new[] { new CanonicalAttachmentInfo("a.pdf", "application/pdf", 16) };

        var result = await NewProvider().BackfillAttachmentsAsync(
            FolderPath.Parse("Inbox"), "DEST", source, missing, CancellationToken.None);

        result.Added.Should().Be(0);
        result.Failed.Should().Be(1);
        result.ErrorCode.Should().Be("graph:attachment-upload-failed");
    }

    [Fact]
    public async Task Empty_missing_list_uploads_nothing()
    {
        StubAttachmentPost();
        var result = await NewProvider().BackfillAttachmentsAsync(
            FolderPath.Parse("Inbox"), "DEST", SourceWith(("a.pdf", "application/pdf")),
            System.Array.Empty<CanonicalAttachmentInfo>(), CancellationToken.None);

        result.Added.Should().Be(0);
        result.Failed.Should().Be(0);
        AttachmentPostCount().Should().Be(0);
    }

    public void Dispose()
    {
        _server.Dispose();
        GC.SuppressFinalize(this);
    }
}
