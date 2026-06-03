using System;
using System.CommandLine;
using System.IO;
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
/// End-to-end (USER-GATE precursor): proves <c>emaigrator preflight</c> then <c>emaigrator run</c>
/// migrate real messages over GreenMail and exit 0 — driving the CLI's real composition root
/// (CliHostBuilder) + in-process worker against live Postgres/RabbitMQ/Redis containers.
/// </summary>
[Collection("cli-e2e")]
public sealed class PreflightRunE2ETests : IAsyncLifetime
{
    private const int SeedCount = 20;

    private readonly GreenMailCliFixture _fx;
    private readonly ITestOutputHelper _log;
    private string _sourceUser = "";
    private string _destUser = "";

    public PreflightRunE2ETests(GreenMailCliFixture fx, ITestOutputHelper log)
    {
        _fx = fx;
        _log = log;
    }

    // Dedicated mailbox pair per class → the shared single GreenMail cannot leak another class's mail.
    public async Task InitializeAsync() =>
        (_sourceUser, _destUser) = await _fx.CreateMailboxPairAsync("preflight");

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedSourceAsync()
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", _fx.ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(_sourceUser, GreenMailCliFixture.SourcePassword);
        var inbox = client.Inbox!;
        await inbox.OpenAsync(FolderAccess.ReadWrite);
        for (var i = 0; i < SeedCount; i++)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress("Sender", "sender@x.com"));
            msg.To.Add(new MailboxAddress("Dest", _destUser));
            msg.Subject = $"Seed {i}";
            msg.MessageId = $"<seed-{i}@greenmail.local>";
            msg.Body = new TextPart("plain") { Text = $"Body {i}" };
            await inbox.AppendAsync(new AppendRequest(msg, MessageFlags.Seen, DateTimeOffset.UtcNow));
        }

        await client.DisconnectAsync(true);

        // Ensure all seeded messages are visible to a fresh IMAP session before any preflight/run lists them.
        await CliMailbox.WaitUntilCountAtLeastAsync(
            _fx.ImapPort, _sourceUser, GreenMailCliFixture.SourcePassword, SeedCount, TimeSpan.FromSeconds(30));
    }

    private Task<int> WaitDestAsync(int expected) =>
        CliMailbox.WaitUntilCountAsync(
            _fx.ImapPort, _destUser, GreenMailCliFixture.DestPassword, expected, TimeSpan.FromSeconds(60));

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

    [Fact]
    public async Task Preflight_then_run_migrates_all_messages_and_exits_zero()
    {
        await SeedSourceAsync();
        var dir = Directory.CreateTempSubdirectory("emaigrator-cli-e2e").FullName;
        var profile = CliProfiles.WriteImapToImap(dir, _fx.ImapPort, _sourceUser, _destUser);

        try
        {
            // 1. Preflight: read-only scan must see exactly the 20 seeded source messages.
            var (preExit, preOut) = await InvokeCliAsync(["preflight", "--profile", profile, "--json"]);
            _log.WriteLine($"--- preflight (exit {preExit}) ---\n{preOut}");
            preExit.Should().Be((int)CliExitCode.Success, "preflight should succeed; output:\n{0}", preOut);
            // JsonOutputWriter is camelCase + indented → a space follows the colon.
            preOut.Should().Contain("\"messageCount\": 20");

            // 2. Run: create the migration from the profile, enqueue, and block until terminal.
            var (runExit, runOut) = await InvokeCliAsync(["run", "--profile", profile, "--json"]);
            _log.WriteLine($"--- run (exit {runExit}) ---\n{runOut}");
            runExit.Should().Be((int)CliExitCode.Success, "run should reach Completed; output:\n{0}", runOut);

            // 3. The destination mailbox must settle at all 20 migrated messages.
            (await WaitDestAsync(SeedCount)).Should().Be(SeedCount);

            // 4. Security: no plaintext password ever appears in CLI output.
            (preOut + runOut).Should().NotContain(GreenMailCliFixture.SourcePassword)
                                       .And.NotContain(GreenMailCliFixture.DestPassword);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
