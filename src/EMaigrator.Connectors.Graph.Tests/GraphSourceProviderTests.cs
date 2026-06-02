using System.Collections.Generic;
using EMaigrator.Connectors.Graph;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphSourceProviderTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    // Fixtures are inlined as constants rather than loaded from disk: the test csproj does not
    // copy a Fixtures\** item to the output directory, so File.ReadAllText(AppContext.BaseDirectory)
    // would fail at runtime. The content matches Fixtures/folders_list.json and
    // Fixtures/messages_inbox.json verbatim.
    private const string FoldersListJson = """
        {
          "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users('user')/mailFolders",
          "value": [
            { "id": "inbox-id", "displayName": "Inbox", "parentFolderId": "msgfolderroot", "totalItemCount": 2, "childFolderCount": 1 },
            { "id": "projects-id", "displayName": "Projects", "parentFolderId": "inbox-id", "totalItemCount": 1, "childFolderCount": 0 },
            { "id": "sent-id", "displayName": "Sent Items", "parentFolderId": "msgfolderroot", "totalItemCount": 0, "childFolderCount": 0 },
            { "id": "drafts-id", "displayName": "Drafts", "parentFolderId": "msgfolderroot", "totalItemCount": 0, "childFolderCount": 0 }
          ]
        }
        """;

    private const string MessagesInboxJson = """
        {
          "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#users('user')/mailFolders('inbox-id')/messages",
          "value": [
            {
              "id": "msg-1",
              "internetMessageId": "<m1@contoso.com>",
              "subject": "First",
              "receivedDateTime": "2026-05-01T09:30:00Z",
              "sentDateTime": "2026-04-30T18:00:00Z",
              "isRead": true,
              "isDraft": false,
              "categories": ["Finance"],
              "body": { "contentType": "text", "content": "hello" }
            },
            {
              "id": "msg-2",
              "internetMessageId": "<m2@contoso.com>",
              "subject": "Second",
              "receivedDateTime": "2026-05-02T10:00:00Z",
              "sentDateTime": "2026-05-02T09:00:00Z",
              "isRead": false,
              "isDraft": false,
              "categories": [],
              "body": { "contentType": "text", "content": "world" }
            }
          ]
        }
        """;

    private GraphSourceProvider NewProvider()
    {
        var client = GraphTestClientFactory.Create(_server.Url!);
        return new GraphSourceProvider(client, "user@contoso.com");
    }

    private void StubFolders() =>
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(FoldersListJson));

    private void StubMessages() =>
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/inbox-id/messages").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(MessagesInboxJson));

    private void StubMime(string msgId, string mime) =>
        _server.Given(Request.Create()
                   .WithPath($"/v1.0/users/user@contoso.com/messages/{msgId}/$value").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "text/plain").WithBody(mime));

    [Fact]
    public void Id_and_constraints_are_graph_ms365()
    {
        var p = NewProvider();
        p.Id.Value.Should().Be("graph");
        p.Constraints.Should().BeSameAs(GraphConstraints.MS365);
    }

    [Fact]
    public async Task TestConnection_ok_counts_folders()
    {
        StubFolders();
        var result = await NewProvider().TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.FolderCount.Should().Be(4);
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task TestConnection_failure_returns_normalized_error_code()
    {
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(403)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("{\"error\":{\"code\":\"ErrorAccessDenied\",\"message\":\"denied\"}}"));

        var result = await NewProvider().TestConnectionAsync(CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().Be("graph:403:ErrorAccessDenied");
    }

    [Fact]
    public async Task ListFolders_returns_canonical_paths()
    {
        StubFolders();
        var folders = await NewProvider().ListFoldersAsync(CancellationToken.None);

        var paths = folders.Select(f => f.Path.ToString()).ToArray();
        paths.Should().Contain("Inbox");
        paths.Should().Contain("Inbox/Projects");
    }

    [Fact]
    public async Task ReadMessages_yields_canonical_messages_with_mime_stream()
    {
        StubFolders();
        StubMessages();
        StubMime("msg-1", "Message-ID: <m1@contoso.com>\r\n\r\nbody one");
        StubMime("msg-2", "Message-ID: <m2@contoso.com>\r\n\r\nbody two");

        var read = new List<CanonicalMessage>();
        await foreach (var m in NewProvider().ReadMessagesAsync(FolderPath.Parse("Inbox"), new ReadOptions(), CancellationToken.None))
        {
            read.Add(m);
        }

        read.Should().HaveCount(2);
        read[0].MessageId.Should().Be("<m1@contoso.com>");
        read[0].InternalDate.Should().Be(new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero));

        await using var stream = await read[0].OpenContentAsync(CancellationToken.None);
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().Contain("body one");
    }

    [Fact]
    public async Task ReadMessages_applies_since_filter()
    {
        StubFolders();
        StubMessages();

        var since = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await foreach (var _ in NewProvider().ReadMessagesAsync(FolderPath.Parse("Inbox"), new ReadOptions { Since = since }, CancellationToken.None))
        {
        }

        var requests = _server.LogEntries
            .Select(e => e.RequestMessage?.RawQuery ?? string.Empty);
        requests.Any(q => q.Contains("receivedDateTime", StringComparison.Ordinal) && q.Contains("ge", StringComparison.Ordinal)).Should().BeTrue();
    }

    public void Dispose()
    {
        _server.Dispose();
        GC.SuppressFinalize(this);
    }
}
