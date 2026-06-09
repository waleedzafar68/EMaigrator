using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphScanFolderTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private const string FoldersListJson = """
        {
          "value": [
            { "id": "inbox-id", "displayName": "Inbox", "parentFolderId": "msgfolderroot", "totalItemCount": 0, "childFolderCount": 1 },
            { "id": "projects-id", "displayName": "Projects", "parentFolderId": "inbox-id", "totalItemCount": 2, "childFolderCount": 0 }
          ]
        }
        """;

    private const string MessagesJson = """
        {
          "value": [
            { "id": "msg1", "internetMessageId": "<m1@contoso.com>", "hasAttachments": true },
            { "id": "msg2", "internetMessageId": "<m2@contoso.com>", "hasAttachments": false }
          ]
        }
        """;

    private const string Msg1AttachmentsJson = """
        { "value": [
          { "@odata.type": "#microsoft.graph.fileAttachment", "id": "att1", "name": "a.pdf", "contentType": "application/pdf", "size": 100, "isInline": false }
        ] }
        """;

    private GraphDestinationProvider NewProvider()
        => new(GraphTestClientFactory.Create(_server.Url!), "user@contoso.com");

    private void StubFolders() =>
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(FoldersListJson));

    [Fact]
    public async Task Scan_yields_one_digest_per_message_with_attachment_meta_only_when_present()
    {
        StubFolders();
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/messages").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(MessagesJson));
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/messages/msg1/attachments").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(Msg1AttachmentsJson));

        var digests = new List<DestMessageDigest>();
        await foreach (var d in NewProvider().ScanFolderAsync(FolderPath.Parse("Inbox/Projects"), null, null, CancellationToken.None))
        {
            digests.Add(d);
        }

        digests.Should().HaveCount(2);
        var m1 = digests.Single(d => d.InternetMessageId == "<m1@contoso.com>");
        m1.DestMessageId.Should().Be("msg1");
        m1.Attachments.Should().ContainSingle().Which.FileName.Should().Be("a.pdf");

        var m2 = digests.Single(d => d.InternetMessageId == "<m2@contoso.com>");
        m2.Attachments.Should().BeEmpty();

        // No attachment call was made for the message without attachments.
        _server.LogEntries.Count(e =>
            e.RequestMessage?.Path?.EndsWith("/messages/msg2/attachments", StringComparison.Ordinal) ?? false)
            .Should().Be(0);
    }

    [Fact]
    public async Task Scan_with_since_date_filters_dest_by_receivedDateTime_and_select_omits_contentId()
    {
        StubFolders();
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/messages").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(MessagesJson));
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/messages/msg1/attachments").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(Msg1AttachmentsJson));

        var since = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var digests = new List<DestMessageDigest>();
        await foreach (var d in NewProvider().ScanFolderAsync(FolderPath.Parse("Inbox/Projects"), since, null, CancellationToken.None))
        {
            digests.Add(d);
        }

        digests.Should().HaveCount(2);
        // The dest scan is restricted to the received-date window when one is set.
        _server.LogEntries.Any(e => (e.RequestMessage?.RawQuery ?? "").Contains("receivedDateTime", StringComparison.Ordinal))
            .Should().BeTrue("a date-scoped reconcile must filter the dest scan by receivedDateTime");
        // Regression: the attachment metadata $select must NOT request 'contentId' — it is not a property of
        // the base microsoft.graph.attachment type and 400s the entire scan against live Graph.
        _server.LogEntries.Any(e => (e.RequestMessage?.RawQuery ?? "").Contains("contentId", StringComparison.Ordinal))
            .Should().BeFalse("contentId is invalid on the base attachment type");
    }

    [Fact]
    public async Task Scan_yields_nothing_for_missing_folder()
    {
        StubFolders(); // does not contain "Archive"

        var digests = new List<DestMessageDigest>();
        await foreach (var d in NewProvider().ScanFolderAsync(FolderPath.Parse("Archive"), null, null, CancellationToken.None))
        {
            digests.Add(d);
        }

        digests.Should().BeEmpty();
    }

    public void Dispose()
    {
        _server.Dispose();
        GC.SuppressFinalize(this);
    }
}
