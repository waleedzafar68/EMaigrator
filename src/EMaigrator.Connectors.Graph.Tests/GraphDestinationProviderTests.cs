using System.Text;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EMaigrator.Connectors.Graph.Tests;

public class GraphDestinationProviderTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    // Fixtures are inlined as constants rather than loaded from disk: the test csproj does not
    // copy a Fixtures\** item to the output directory, so File.ReadAllText(AppContext.BaseDirectory)
    // would fail at runtime. The content matches Fixtures/folders_list.json,
    // Fixtures/created_folder.json, Fixtures/created_message.json and Fixtures/exists_match.json verbatim.
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

    private const string CreatedFolderJson =
        """{ "id": "new-folder-id", "displayName": "Projects", "parentFolderId": "inbox-id", "totalItemCount": 0 }""";

    private const string CreatedMessageJson =
        """{ "id": "imported-msg-id", "internetMessageId": "<m1@contoso.com>", "subject": "First" }""";

    private const string ExistsMatchJson =
        """{ "value": [ { "id": "found-msg-id", "internetMessageId": "<m1@contoso.com>" } ] }""";

    private GraphDestinationProvider NewProvider()
        => new(GraphTestClientFactory.Create(_server.Url!), "user@contoso.com");

    private void StubFolders() =>
        _server.Given(Request.Create().WithPath("/v1.0/users/user@contoso.com/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(FoldersListJson));

    private static CanonicalMessage Message() => new()
    {
        IdentityKey = "mid:<m1@contoso.com>",
        MessageId = "<m1@contoso.com>",
        InternalDate = new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero),
        OpenContentAsync = ct => Task.FromResult<Stream>(
            new MemoryStream(Encoding.ASCII.GetBytes("Message-ID: <m1@contoso.com>\r\n\r\nbody"))),
    };

    [Fact]
    public async Task EnsureFolder_creates_only_missing_segments()
    {
        StubFolders(); // Inbox + Inbox/Projects already exist
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/childFolders").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json").WithBody(CreatedFolderJson));

        await NewProvider().EnsureFolderAsync(FolderPath.Parse("Inbox/Projects/2026"), CancellationToken.None);

        var posts = _server.LogEntries.Where(e => e.RequestMessage?.Method == "POST").ToArray();
        posts.Should().HaveCount(1); // only "2026" under Projects is created
        posts[0].RequestMessage!.Path.Should().Contain("/mailFolders/projects-id/childFolders");
    }

    [Fact]
    public async Task WriteMessage_imports_mime_and_returns_dest_id()
    {
        StubFolders();
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/messages").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(201)
                   .WithHeader("Content-Type", "application/json").WithBody(CreatedMessageJson));

        var result = await NewProvider().WriteMessageAsync(
            FolderPath.Parse("Inbox/Projects"), Message(), CancellationToken.None);

        result.Written.Should().BeTrue();
        result.DestMessageId.Should().Be("imported-msg-id");

        var post = _server.LogEntries.Single(e => e.RequestMessage?.Method == "POST");
        post.RequestMessage!.Headers!["Content-Type"].First().Should().Contain("text/plain");

        // The posted body is base64 that decodes back to the source MIME (no body persisted anywhere).
        var postedBase64 = post.RequestMessage.Body!;
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(postedBase64));
        decoded.Should().Be("Message-ID: <m1@contoso.com>\r\n\r\nbody");
    }

    [Fact]
    public async Task WriteMessage_throttled_returns_normalized_error_without_tenant()
    {
        StubFolders();
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/messages").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(429)
                   .WithHeader("Content-Type", "application/json").WithHeader("Retry-After", "12")
                   .WithBody("{\"error\":{\"code\":\"errorThrottledRequest\",\"message\":\"throttled tenant 11111111\"}}"));

        var result = await NewProvider().WriteMessageAsync(
            FolderPath.Parse("Inbox/Projects"), Message(), CancellationToken.None);

        result.Written.Should().BeFalse();
        result.ErrorCode.Should().Be("graph:429:throttled");
        result.ErrorCode.Should().NotContain("11111111");
    }

    [Fact]
    public async Task ExistsByMessageId_true_when_match()
    {
        StubFolders();
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/messages")
                   // The SDK URL-encodes the OData '$' to %24, so WireMock sees the param key as
                   // "%24filter" (established Task 6 fact). Match the encoded key so the stub applies.
                   .WithParam("%24filter").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(ExistsMatchJson));

        var exists = await NewProvider().ExistsByMessageIdAsync(
            FolderPath.Parse("Inbox/Projects"), "<m1@contoso.com>", CancellationToken.None);

        exists.Should().BeTrue();
        _server.LogEntries.Any(e =>
            (e.RequestMessage?.RawQuery ?? "").Contains("internetMessageId", StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsByMessageId_false_when_empty()
    {
        StubFolders();
        _server.Given(Request.Create()
                   .WithPath("/v1.0/users/user@contoso.com/mailFolders/projects-id/messages")
                   // The SDK URL-encodes the OData '$' to %24, so WireMock sees the param key as
                   // "%24filter" (established Task 6 fact). Match the encoded key so the stub applies.
                   .WithParam("%24filter").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody("{\"value\":[]}"));

        (await NewProvider().ExistsByMessageIdAsync(
            FolderPath.Parse("Inbox/Projects"), "<nope@contoso.com>", CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public void Id_and_constraints_are_graph_ms365()
    {
        var p = NewProvider();
        p.Id.Value.Should().Be("graph");
        p.Constraints.Should().BeSameAs(GraphConstraints.MS365);
    }

    public void Dispose()
    {
        _server.Dispose();
        GC.SuppressFinalize(this);
    }
}
