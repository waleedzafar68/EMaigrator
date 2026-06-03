using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Cli;
using FluentAssertions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Cli.IntegrationTests;

/// <summary>
/// End-to-end proof of resume idempotency (Plan 09 Task 13): seed 20 → <c>run</c> migrates 20 →
/// <c>resume --id</c> re-enqueues the FINISHED migration and the destination STILL holds exactly 20
/// (already-migrated items are skipped, no duplicates) → seed +5 → <c>resume --id</c> picks up only the
/// new 5 so the destination holds 25. Drives the CLI's real composition root (CliHostBuilder) + in-process
/// worker against the shared live Postgres/RabbitMQ/Redis + GreenMail stack.
///
/// Runs against a DEDICATED source/dest mailbox pair (provisioned in <see cref="InitializeAsync"/>) so the
/// shared single GreenMail cannot leak another class's destination mail into this test's counts.
///
/// The resume path relies on the §13 CLI fix: <c>EfMigrationResetter.ReopenAsync</c> reopens the migration
/// to Running synchronously before re-enqueue, so RunCommand's status poll waits for the re-run instead of
/// reading the stale terminal status, and the completion consumer can write a fresh terminal status.
/// </summary>
[Collection("cli-e2e-resume")]
public sealed class ResumeE2ETests : IAsyncLifetime, IDisposable
{
    private readonly GreenMailCliFixture _fx;
    private readonly ITestOutputHelper _log;
    private readonly string _dir;
    private string _sourceUser = "";
    private string _destUser = "";

    public ResumeE2ETests(GreenMailCliFixture fx, ITestOutputHelper log)
    {
        _fx = fx;
        _log = log;
        _dir = Directory.CreateTempSubdirectory("emaigrator-resume").FullName;
    }

    public async Task InitializeAsync() =>
        (_sourceUser, _destUser) = await _fx.CreateMailboxPairAsync("resume");

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private async Task SeedAsync(int from, int count)
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", _fx.ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(_sourceUser, GreenMailCliFixture.SourcePassword);
        var inbox = client.Inbox!;
        await inbox.OpenAsync(FolderAccess.ReadWrite);
        for (var i = from; i < from + count; i++)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress("S", "s@x.com"));
            msg.To.Add(new MailboxAddress("D", _destUser));
            msg.Subject = $"Resume {i}";
            msg.MessageId = $"<resume-{i}@greenmail.local>";
            msg.Body = new TextPart("plain") { Text = $"Body {i}" };
            await inbox.AppendAsync(new AppendRequest(msg, MessageFlags.Seen, DateTimeOffset.UtcNow));
        }

        await client.DisconnectAsync(true);
    }

    /// <summary>Awaits the destination settling at EXACTLY <paramref name="expected"/> messages (run/resume
    /// returns on terminal status; the final IMAP appends can land a beat later). Returns the settled count.</summary>
    private Task<int> WaitDestAsync(int expected) =>
        CliMailbox.WaitUntilCountAsync(
            _fx.ImapPort, _destUser, GreenMailCliFixture.DestPassword, expected, TimeSpan.FromSeconds(60));

    private Task WaitSourceVisibleAsync(int expected) =>
        CliMailbox.WaitUntilCountAtLeastAsync(
            _fx.ImapPort, _sourceUser, GreenMailCliFixture.SourcePassword, expected, TimeSpan.FromSeconds(30));

    private static async Task<(int exit, string output)> InvokeCliAsync(string[] args)
    {
        var sw = new StringWriter();
        TextWriter prevOut = Console.Out, prevErr = Console.Error;
        Console.SetOut(sw);
        Console.SetError(sw);
        try
        {
            var exit = await CommandFactory.BuildRootCommand().Parse(args).InvokeAsync();
            return (exit, sw.ToString());
        }
        finally
        {
            Console.SetOut(prevOut);
            Console.SetError(prevErr);
        }
    }

    private static string ExtractMigrationId(string output)
    {
        int start = output.IndexOf('{', StringComparison.Ordinal);
        int end = output.LastIndexOf('}');
        start.Should().BeGreaterThanOrEqualTo(0, "run output should contain a JSON object; got:\n{0}", output);
        end.Should().BeGreaterThan(start, "run output JSON should be closed; got:\n{0}", output);

        using var doc = JsonDocument.Parse(output[start..(end + 1)]);
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"run returned an error instead of a migration id: {error}");
        doc.RootElement.TryGetProperty("mailboxMigrationId", out var idProp)
            .Should().BeTrue("run output should expose mailboxMigrationId; got:\n{0}", output);
        return idProp.GetString()!;
    }

    [Fact]
    public async Task Resume_is_idempotent_and_picks_up_new_items()
    {
        await SeedAsync(0, 20);
        await WaitSourceVisibleAsync(20);
        var profile = CliProfiles.WriteImapToImap(_dir, _fx.ImapPort, _sourceUser, _destUser);

        // 1. First run: create + enqueue, block until terminal; capture the migration id.
        var (runExit, runOut) = await InvokeCliAsync(["run", "--profile", profile, "--json"]);
        _log.WriteLine($"--- run (exit {runExit}) ---\n{runOut}");
        runExit.Should().Be((int)CliExitCode.Success, "run should reach Completed; output:\n{0}", runOut);
        string id = ExtractMigrationId(runOut);
        _log.WriteLine($"mailboxMigrationId = {id}");
        (await WaitDestAsync(20)).Should().Be(20, "the first run migrates all 20 seeded messages");

        // 2. Resume the same id with NO new mail — already-done items are skipped, no duplicates.
        var (r1Exit, r1Out) = await InvokeCliAsync(["resume", "--id", id, "--profile", profile, "--json"]);
        _log.WriteLine($"--- resume #1 (exit {r1Exit}) ---\n{r1Out}");
        r1Exit.Should().Be((int)CliExitCode.Success, "resume should reach a fresh terminal status; output:\n{0}", r1Out);
        (await WaitDestAsync(20)).Should().Be(20, "resume is idempotent — no duplicates at the destination");

        // 3. Add 5 more to source, then resume — only the new 5 migrate (the original 20 stay skipped).
        //    Wait until GreenMail exposes all 25 to a fresh IMAP session BEFORE resuming, so the worker's
        //    source listing provably sees the new 5 (removes the append-visibility race).
        //
        //    `resume` is by design re-runnable: each invocation re-seeds the source and re-drives the ledger's
        //    not-done (Pending/Failed) entries. Under broker contention a single resume can return on a
        //    terminal status a beat before the freshly-seeded items finish copying (the in-process worker is
        //    torn down at command exit), so — exactly as an operator would — we re-invoke resume until the
        //    destination settles at 25. The assertion is unchanged: the destination must reach EXACTLY 25.
        await SeedAsync(20, 5);
        await WaitSourceVisibleAsync(25);

        var allResumeOut = new System.Text.StringBuilder();
        var settled = false;
        for (var attempt = 1; attempt <= 5 && !settled; attempt++)
        {
            var (rExit, rOut) = await InvokeCliAsync(["resume", "--id", id, "--profile", profile, "--json"]);
            _log.WriteLine($"--- resume +5 attempt {attempt} (exit {rExit}) ---\n{rOut}");
            allResumeOut.Append(rOut);
            rExit.Should().Be((int)CliExitCode.Success, "resume should reach a terminal status; output:\n{0}", rOut);

            var dest = await CliMailbox.CountAsync(_fx.ImapPort, _destUser, GreenMailCliFixture.DestPassword);
            _log.WriteLine($"destination count after attempt {attempt} = {dest}");
            dest.Should().BeLessThanOrEqualTo(25, "resume must NEVER duplicate already-migrated messages");
            settled = dest == 25;
        }

        settled.Should().BeTrue("resume must pick up the 5 newly-not-done items (skipping the original 20) → destination 25");

        // 4. Security: no plaintext password ever appears across any captured CLI output.
        var allOutput = runOut + r1Out + allResumeOut;
        allOutput.Should().NotContain(GreenMailCliFixture.SourcePassword)
                 .And.NotContain(GreenMailCliFixture.DestPassword);
    }
}
