using System.Globalization;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;

namespace EMaigrator.Connectors.Imap.IntegrationTests;

[Collection("greenmail")]
public class ImapRoundtripFunctionalTests
{
    private readonly GreenMailImapFixture _fx;
    public ImapRoundtripFunctionalTests(GreenMailImapFixture fx) => _fx = fx;

    private ConnectionDescriptor Descriptor() => new()
    {
        Provider = new ProviderId("imap"),
        Auth = AuthMethod.ImapBasic,
        Settings = new Dictionary<string, string>
        {
            ["preset"] = "custom",
            ["host"] = _fx.Host,
            ["port"] = _fx.ImapPort.ToString(CultureInfo.InvariantCulture),
            ["useSsl"] = "false",
            ["allowPlaintext"] = "true",
            ["accountEmail"] = GreenMailImapFixture.UserEmail,
        },
    };

    private static SecretBundle Secret() =>
        new(new Dictionary<string, string> { ["password"] = GreenMailImapFixture.Password });

    // The shared GreenMail container exposes BOTH the real source folders
    // ("INBOX", "INBOX/FuncProjects") and the destination tree the copy creates at
    // the namespace root ("Migrated/INBOX", "Migrated/FuncProjects"). A source +
    // dest folder can share a leaf Name, so we select sources by FULL canonical path
    // and exclude the Migrated tree.
    private async Task<long> CopyAsync(bool dedup)
    {
        long written = 0;
        await using var src = new ImapSourceProvider(Descriptor(), Secret());
        await using var dst = new ImapDestinationProvider(Descriptor(), Secret());

        var folders = await src.ListFoldersAsync(CancellationToken.None);
        var sources = folders.Where(f =>
            f.Path.ToString() == "INBOX" || f.Path.ToString() == "INBOX/FuncProjects");

        foreach (var folder in sources)
        {
            var destPath = new FolderPath(new[] { "Migrated", folder.Path.Name });
            await dst.EnsureFolderAsync(destPath, CancellationToken.None);
            await foreach (var msg in src.ReadMessagesAsync(folder.Path, new ReadOptions(), CancellationToken.None))
            {
                if (dedup && msg.MessageId is not null &&
                    await dst.ExistsByMessageIdAsync(destPath, msg.MessageId, CancellationToken.None))
                    continue;
                var r = await dst.WriteMessageAsync(destPath, msg, CancellationToken.None);
                if (r.Written) written++;
            }
        }
        return written;
    }

    // Count by FULL canonical path (e.g. "Migrated/INBOX") — matching by leaf name
    // would be ambiguous with the real source "INBOX".
    private async Task<long> CountAsync(string fullPath)
    {
        await using var src = new ImapSourceProvider(Descriptor(), Secret());
        var folders = await src.ListFoldersAsync(CancellationToken.None);
        var folder = folders.FirstOrDefault(f => f.Path.ToString() == fullPath);
        return folder?.EstimatedMessageCount ?? 0;
    }

    [Fact]
    public async Task Roundtrip_copies_all_messages_then_reruns_with_zero_duplicates()
    {
        var run = Guid.NewGuid().ToString("N").Substring(0, 6);
        for (var i = 0; i < 5; i++)
            await _fx.AppendAsync("INBOX", $"inbox-{run}-{i}", "b", $"<ib-{run}-{i}@local.test>",
                MailKit.MessageFlags.Seen, DateTimeOffset.UtcNow.AddDays(-i));
        for (var i = 0; i < 3; i++)
            await _fx.AppendAsync("FuncProjects", $"proj-{run}-{i}", "b", $"<pj-{run}-{i}@local.test>",
                MailKit.MessageFlags.Flagged, DateTimeOffset.UtcNow.AddDays(-i));

        // First copy pass (no dedup): everything is written.
        var firstWritten = await CopyAsync(dedup: false);
        firstWritten.Should().BeGreaterThanOrEqualTo(8);

        var inboxCountAfterFirst = await CountAsync("Migrated/INBOX");
        var projCountAfterFirst = await CountAsync("Migrated/FuncProjects");
        inboxCountAfterFirst.Should().BeGreaterThanOrEqualTo(5);
        projCountAfterFirst.Should().BeGreaterThanOrEqualTo(3);

        // Second pass WITH dedup: ExistsByMessageId short-circuits each -> zero writes.
        var secondWritten = await CopyAsync(dedup: true);
        secondWritten.Should().Be(0);

        // Dest counts unchanged across the two passes (idempotent re-append: no duplicates).
        (await CountAsync("Migrated/INBOX")).Should().Be(inboxCountAfterFirst);
        (await CountAsync("Migrated/FuncProjects")).Should().Be(projCountAfterFirst);
    }

    [Fact]
    public async Task IdentityKey_is_stable_across_reads()
    {
        var mid = $"<stable-{Guid.NewGuid():N}@local.test>";
        await _fx.AppendAsync("INBOX", $"stable-{mid}", "b", mid, MailKit.MessageFlags.None, DateTimeOffset.UtcNow);

        // The source provider exposes the canonical (angle-bracket-stripped) Message-ID
        // form — matching MailKit's Envelope.MessageId and the providers' own
        // messageId.Trim('<','>') convention. Compare on that canonical form.
        var canonicalMid = mid.Trim('<', '>');

        async Task<string?> ReadKey()
        {
            await using var src = new ImapSourceProvider(Descriptor(), Secret());
            await foreach (var m in src.ReadMessagesAsync(FolderPath.Parse("INBOX"), new ReadOptions(), CancellationToken.None))
                if (m.MessageId == canonicalMid) return m.IdentityKey;
            return null;
        }

        var k1 = await ReadKey();
        var k2 = await ReadKey();
        k1.Should().NotBeNull();
        k1.Should().Be(k2);
        k1!.Should().StartWith("mid:");
    }
}
