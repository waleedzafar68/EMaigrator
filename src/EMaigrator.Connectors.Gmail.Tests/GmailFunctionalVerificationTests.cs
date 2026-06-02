using EMaigrator.Connectors.Gmail;
using EMaigrator.Core.Model;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace EMaigrator.Connectors.Gmail.Tests;

/// <summary>
/// Functional Verification gate (Plan 06, Task 14): proves the Gmail connector's headline
/// Gmail→Gmail behavior end-to-end against recorded fixtures, entirely offline (two WireMock
/// servers, zero real network). It exercises the full source→canonical→destination path:
/// (1) folder discovery (CHAT excluded), (2) raw read of the <c>Work/Clients/Acme</c> message,
/// (3) destination label creation when absent (labels.create POST observed), (4) raw import with
/// <c>internalDateSource=dateHeader</c> and the <c>UNREAD</c> label (message is unread), and
/// (5) dedup-by-Message-ID via rfc822msgid search. Message bytes are materialized only through
/// in-memory <see cref="MemoryStream"/>s — never persisted to disk (DESIGN.md §6/§10).
/// </summary>
public class GmailFunctionalVerificationTests
{
    [Fact]
    public async Task EndToEnd_DiscoverReadEnsureImportDedup()
    {
        // ---------- SOURCE: labels (folders) + messages list + raw message get ----------
        using var srcFx = new GmailWireMockFixture();
        srcFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(GmailWireMockFixture.Fixture("labels.list.json")));
        srcFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(GmailWireMockFixture.Fixture("messages.list.json")));
        srcFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(GmailWireMockFixture.Fixture("messages.get.raw.json")));

        await using var source = new GmailSourceProvider(srcFx.CreateService(), "me");

        // (1) Folder discovery: the mappable labels include Work/Clients/Acme and exclude CHAT.
        var folders = await source.ListFoldersAsync(CancellationToken.None);
        var folderPaths = folders.Select(f => f.Path.ToString()).ToList();
        folderPaths.Should().Contain("Work/Clients/Acme");
        folderPaths.Should().NotContain("CHAT");

        // (2) Read the fixture message (id Label_12 = Work/Clients/Acme) as a canonical message.
        CanonicalMessage? msg = null;
        await foreach (var m in source.ReadMessagesAsync(
            FolderPath.Parse("Work/Clients/Acme"), new(), CancellationToken.None))
        {
            msg = m;
            break;
        }

        msg.Should().NotBeNull();
        msg!.MessageId.Should().Be("<acme-001@example.com>");
        msg.Flags.Should().NotHaveFlag(MessageFlags.Seen); // UNREAD in fixture

        // No-disk guarantee: content is only ever exposed via an in-memory MemoryStream.
        await using (var content = await msg.OpenContentAsync(CancellationToken.None))
        {
            content.Should().BeOfType<MemoryStream>();
        }

        // ---------- DESTINATION: target label is ABSENT, so EnsureFolderAsync must create it ----------
        using var dstFx = new GmailWireMockFixture();
        // labels.list returns the SAME fixture set, which does NOT contain "Migrated/Acme".
        dstFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(GmailWireMockFixture.Fixture("labels.list.json")));
        dstFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(GmailWireMockFixture.Fixture("labels.create.json")));
        dstFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages/import").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(GmailWireMockFixture.Fixture("messages.import.json")));
        // rfc822msgid dedup search hits the messages.list GET endpoint; a non-empty result == "exists".
        dstFx.Server.Given(Request.Create().WithPath("/gmail/v1/users/me/messages").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(GmailWireMockFixture.Fixture("messages.list.json")));

        await using var dest = new GmailDestinationProvider(dstFx.CreateService(), "me");

        // (3) Ensure the destination label: absent => a labels.create POST is issued.
        await dest.EnsureFolderAsync(FolderPath.Parse("Migrated/Acme"), CancellationToken.None);
        dstFx.Server.LogEntries.Should().Contain(e =>
            e.RequestMessage!.Method == "POST" &&
            e.RequestMessage!.Path == "/gmail/v1/users/me/labels");

        // (4) Import the raw message into the destination label.
        var write = await dest.WriteMessageAsync(
            FolderPath.Parse("Migrated/Acme"), msg, CancellationToken.None);
        write.Written.Should().BeTrue();
        write.DestMessageId.Should().Be("18f0bb33dd44ee01");

        var import = Array.Find(
            dstFx.Server.LogEntries.ToArray(),
            e => e.RequestMessage!.Path == "/gmail/v1/users/me/messages/import");
        import.Should().NotBeNull();
        // Original sent date is preserved via the date-header source...
        import!.RequestMessage!.RawQuery.Should().Contain("internalDateSource=dateHeader");
        // ...and the unread state is carried as the UNREAD label on the import body.
        (import.RequestMessage!.Body ?? "").Should().Contain("UNREAD");

        // (5) Dedup-by-Message-ID: the rfc822msgid search now reports the message as present.
        var exists = await dest.ExistsByMessageIdAsync(
            FolderPath.Parse("Migrated/Acme"), msg.MessageId!, CancellationToken.None);
        exists.Should().BeTrue();

        var search = Array.Find(
            dstFx.Server.LogEntries.ToArray(),
            e => e.RequestMessage!.Method == "GET" &&
                 e.RequestMessage!.Path == "/gmail/v1/users/me/messages");
        search!.RequestMessage!.RawQuery.Should().Contain("rfc822msgid");
    }
}
