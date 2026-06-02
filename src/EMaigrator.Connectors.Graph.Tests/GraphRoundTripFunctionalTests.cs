using System.Text;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace EMaigrator.Connectors.Graph.Tests;

/// <summary>
/// Functional Verification gate (Plan 05, Task 10): proves the headline WorkMail→MS365 wedge end-to-end
/// against recorded-fixture Graph endpoints, entirely offline (two WireMock servers, no live calls).
/// A message is read from a source mailbox folder and MIME-imported into a destination folder,
/// demonstrating (1) folder resolution, (2) lossless streaming MIME pass-through — the bytes POSTed to
/// the destination decode back to the exact source MIME with no truncation, re-encoding, or disk
/// buffering — and (3) dedup-by-Message-ID wiring (DESIGN.md §4.1/§6/§10/§17).
/// </summary>
public class GraphRoundTripFunctionalTests : IDisposable
{
    private readonly WireMockServer _source = WireMockServer.Start();
    private readonly WireMockServer _dest = WireMockServer.Start();

    // A single account UPN is used consistently across both servers. The Graph SDK emits the '@' in the
    // UPN unencoded, so WireMock's literal WithPath(".../user@contoso.com/...") matches the request path.
    private const string Account = "user@contoso.com";

    // The known RFC822/MIME byte string the source mailbox returns for msg-1's $value endpoint. The
    // round-trip is "lossless" iff the destination POST body base64-decodes back to exactly these bytes.
    private const string SourceMime =
        "Message-ID: <m1@contoso.com>\r\nSubject: Hi\r\n\r\nbody one";

    // Fixtures are inlined as constants (the test csproj copies no Fixtures\** to output, so reading from
    // AppContext.BaseDirectory would fail at runtime). Content mirrors the project's other Graph fixtures.
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
              "subject": "Hi",
              "receivedDateTime": "2026-05-01T09:30:00Z",
              "sentDateTime": "2026-04-30T18:00:00Z",
              "isRead": true,
              "isDraft": false,
              "categories": ["Finance"],
              "body": { "contentType": "text", "content": "hello" }
            }
          ]
        }
        """;

    private const string CreatedMessageJson =
        """{ "id": "imported-msg-id", "internetMessageId": "<m1@contoso.com>", "subject": "Hi" }""";

    private const string ExistsMatchJson =
        """{ "value": [ { "id": "found-msg-id", "internetMessageId": "<m1@contoso.com>" } ] }""";

    [Fact]
    public async Task Reads_a_message_and_imports_it_losslessly_into_destination()
    {
        // ---------- SOURCE stubs: folders (Inbox resolves) + Inbox messages + msg-1 raw MIME ($value) ----------
        _source.Given(Request.Create()
                   .WithPath($"/v1.0/users/{Account}/mailFolders").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(FoldersListJson));

        _source.Given(Request.Create()
                   .WithPath($"/v1.0/users/{Account}/mailFolders/inbox-id/messages").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(MessagesInboxJson));

        // The SDK's Messages[id].Content builder maps to the .../messages/{id}/$value endpoint.
        _source.Given(Request.Create()
                   .WithPath($"/v1.0/users/{Account}/messages/msg-1/$value").UsingGet())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "text/plain").WithBody(SourceMime));

        // ---------- DEST stubs: folders (Inbox/Projects resolves) + create-message POST ----------
        _dest.Given(Request.Create()
                 .WithPath($"/v1.0/users/{Account}/mailFolders").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(FoldersListJson));

        _dest.Given(Request.Create()
                 .WithPath($"/v1.0/users/{Account}/mailFolders/projects-id/messages").UsingPost())
             .RespondWith(Response.Create().WithStatusCode(201)
                 .WithHeader("Content-Type", "application/json").WithBody(CreatedMessageJson));

        var source = new GraphSourceProvider(GraphTestClientFactory.Create(_source.Url!), Account);
        var dest = new GraphDestinationProvider(GraphTestClientFactory.Create(_dest.Url!), Account);

        // ---------- ACT: read the first source message, then import it into the destination folder ----------
        CanonicalMessage? first = null;
        await foreach (var m in source.ReadMessagesAsync(
            FolderPath.Parse("Inbox"), new ReadOptions(), CancellationToken.None))
        {
            first = m;
            break;
        }

        first.Should().NotBeNull();
        first!.MessageId.Should().Be("<m1@contoso.com>");

        // WriteMessageAsync lazily invokes first.OpenContentAsync, which streams msg-1's $value from the
        // SOURCE server straight into the destination POST — never materializing the body on disk.
        var write = await dest.WriteMessageAsync(
            FolderPath.Parse("Inbox/Projects"), first, CancellationToken.None);

        write.Written.Should().BeTrue();
        write.DestMessageId.Should().Be("imported-msg-id");

        // ---------- ASSERT: the POSTed bytes decode (base64) back to the exact source MIME ----------
        var post = _dest.LogEntries.Single(e => e.RequestMessage?.Method == "POST");
        post.RequestMessage!.Headers!["Content-Type"].First().Should().Contain("text/plain");

        var postedBase64 = post.RequestMessage!.Body!;
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(postedBase64.Trim()));
        decoded.Should().Be(SourceMime);

        // ---------- ASSERT: dedup-by-Message-ID is wired (exists GET now reports the imported message) ----------
        // The SDK URL-encodes the OData '$' to %24, so WireMock sees the param key as "%24filter".
        _dest.Given(Request.Create()
                 .WithPath($"/v1.0/users/{Account}/mailFolders/projects-id/messages")
                 .WithParam("%24filter").UsingGet())
             .RespondWith(Response.Create().WithStatusCode(200)
                 .WithHeader("Content-Type", "application/json").WithBody(ExistsMatchJson));

        var exists = await dest.ExistsByMessageIdAsync(
            FolderPath.Parse("Inbox/Projects"), "<m1@contoso.com>", CancellationToken.None);

        exists.Should().BeTrue();
    }

    public void Dispose()
    {
        _source.Dispose();
        _dest.Dispose();
        GC.SuppressFinalize(this);
    }
}
