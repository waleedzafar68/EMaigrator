using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using EMaigrator.Cli;
using EMaigrator.Cli.Profile;
using FluentAssertions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Xunit;
using Xunit.Abstractions;

namespace EMaigrator.Cli.IntegrationTests;

/// <summary>
/// USER-GATE Functional Verification (Plan 09 Task 15): proves the CLI subsystem's headline
/// operator happy-path end-to-end as one acceptance flow against the live GreenMail + Postgres +
/// RabbitMQ + Redis stack — <c>migration new</c> → (rewrite the profile for GreenMail) →
/// <c>connect test --side from</c> → <c>connect test --side to</c> → <c>preflight --json</c> →
/// <c>run --json</c> → <c>status --id … --json</c> → <c>report --id … --out &lt;csv&gt;</c>, all with
/// correct exit codes, 20 migrated / 0 failed, and a metadata-only CSV. The fixture already sets every
/// EMAIGRATOR_-prefixed environment variable (connection strings, secret store key, SECRET_FROM/_TO,
/// orchestration), so this test sets none of them.
/// </summary>
[Collection("cli-e2e")]
public sealed class FullCliFlowAcceptanceTests : IDisposable
{
    private const int SeedCount = 20;

    private readonly GreenMailCliFixture _fx;
    private readonly ITestOutputHelper _log;
    private readonly string _dir;

    public FullCliFlowAcceptanceTests(GreenMailCliFixture fx, ITestOutputHelper log)
    {
        _fx = fx;
        _log = log;
        _dir = Directory.CreateTempSubdirectory("emaigrator-cli-flow").FullName;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private async Task SeedSourceAsync()
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", _fx.ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(GreenMailCliFixture.SourceUser, GreenMailCliFixture.SourcePassword);
        var inbox = client.Inbox!;
        await inbox.OpenAsync(FolderAccess.ReadWrite);
        for (var i = 0; i < SeedCount; i++)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress("Sender", "sender@x.com"));
            msg.To.Add(new MailboxAddress("Dest", GreenMailCliFixture.DestUser));
            msg.Subject = $"Flow {i}";
            msg.MessageId = $"<flow-{i}@greenmail.local>";
            msg.Body = new TextPart("plain") { Text = $"Body {i}" };
            await inbox.AppendAsync(new AppendRequest(msg, MessageFlags.Seen, DateTimeOffset.UtcNow));
        }

        await client.DisconnectAsync(true);
    }

    private async Task<int> CountDestAsync()
    {
        using var client = new ImapClient();
        await client.ConnectAsync("127.0.0.1", _fx.ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(GreenMailCliFixture.DestUser, GreenMailCliFixture.DestPassword);
        var inbox = client.Inbox!;
        await inbox.OpenAsync(FolderAccess.ReadOnly);
        var count = inbox.Count;
        await client.DisconnectAsync(true);
        return count;
    }

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

    /// <summary>Reads the <c>mailboxMigrationId</c> from a <see cref="EMaigrator.Cli.Output.RunOutput"/>
    /// JSON blob; defensively surfaces the <c>{ "error": ... }</c> shape so a failed run yields a clear
    /// assertion message instead of an opaque <see cref="JsonException"/>.</summary>
    private static string ExtractId(string output)
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
    public async Task Full_cli_flow_new_test_preflight_run_status_report_all_green()
    {
        // 0. Seed exactly 20 source messages over GreenMail (MailKit).
        await SeedSourceAsync();
        var profile = Path.Combine(_dir, "profile.json");
        var csv = Path.Combine(_dir, "report.csv");

        // 1. migration new — scaffolds a starter profile (with EXAMPLE hosts) and exits 0.
        var (newExit, newOut) = await InvokeCliAsync(["migration", "new", "--profile", profile]);
        _log.WriteLine($"--- migration new (exit {newExit}) ---\n{newOut}");
        newExit.Should().Be((int)CliExitCode.Success, "migration new should succeed; output:\n{0}", newOut);
        File.Exists(profile).Should().BeTrue("migration new should have written the profile file");
        ProfileLoader.Load(profile).Ok.Should().BeTrue("the scaffolded profile must round-trip through ProfileLoader");

        // 2. Rewrite the profile for GreenMail (the §D.2 IMAP→IMAP shape) — do NOT rely on the example hosts.
        profile = CliProfiles.WriteImapToImap(_dir, _fx.ImapPort);

        // 3. connect test --side from — exercises the §A secret fix against the REAL IMAP connector.
        var (fromExit, fromOut) = await InvokeCliAsync(["connect", "test", "--side", "from", "--profile", profile, "--json"]);
        _log.WriteLine($"--- connect test from (exit {fromExit}) ---\n{fromOut}");
        fromExit.Should().Be((int)CliExitCode.Success, "connect test --side from should succeed; output:\n{0}", fromOut);

        // 4. connect test --side to.
        var (toExit, toOut) = await InvokeCliAsync(["connect", "test", "--side", "to", "--profile", profile, "--json"]);
        _log.WriteLine($"--- connect test to (exit {toExit}) ---\n{toOut}");
        toExit.Should().Be((int)CliExitCode.Success, "connect test --side to should succeed; output:\n{0}", toOut);

        // 5. preflight --json — read-only scan must see exactly the 20 seeded source messages.
        var (preExit, preOut) = await InvokeCliAsync(["preflight", "--profile", profile, "--json"]);
        _log.WriteLine($"--- preflight (exit {preExit}) ---\n{preOut}");
        preExit.Should().Be((int)CliExitCode.Success, "preflight should succeed; output:\n{0}", preOut);
        preOut.Should().Contain("\"messageCount\": 20");

        // 6. run --json — create + enqueue, block until terminal; capture the migration id.
        var (runExit, runOut) = await InvokeCliAsync(["run", "--profile", profile, "--json"]);
        _log.WriteLine($"--- run (exit {runExit}) ---\n{runOut}");
        runExit.Should().Be((int)CliExitCode.Success, "run should reach Completed with 0 failures; output:\n{0}", runOut);
        string id = ExtractId(runOut);
        _log.WriteLine($"mailboxMigrationId = {id}");

        // 7. Destination mailbox holds all 20 migrated messages.
        (await CountDestAsync()).Should().Be(SeedCount);

        // 8. status --id … --json — terminal counts: migrated == 20, failed == 0.
        var (statusExit, statusOut) = await InvokeCliAsync(["status", "--id", id, "--profile", profile, "--json"]);
        _log.WriteLine($"--- status (exit {statusExit}) ---\n{statusOut}");
        statusExit.Should().Be((int)CliExitCode.Success, "status should succeed; output:\n{0}", statusOut);
        int sStart = statusOut.IndexOf('{', StringComparison.Ordinal);
        int sEnd = statusOut.LastIndexOf('}');
        using (var doc = JsonDocument.Parse(statusOut[sStart..(sEnd + 1)]))
        {
            doc.RootElement.GetProperty("migrated").GetInt64().Should().Be(20, "all 20 messages should be migrated");
            doc.RootElement.GetProperty("failed").GetInt64().Should().Be(0, "no message should have failed");
        }

        // 9. report --id … --out <csv> — metadata-only header; no body/subject/sender/recipient columns.
        var (reportExit, reportOut) = await InvokeCliAsync(["report", "--id", id, "--profile", profile, "--out", csv]);
        _log.WriteLine($"--- report (exit {reportExit}) ---\n{reportOut}");
        reportExit.Should().Be((int)CliExitCode.Success, "report should succeed; output:\n{0}", reportOut);
        File.Exists(csv).Should().BeTrue("report should have written the CSV file");
        string csvText = File.ReadAllText(csv);
        _log.WriteLine($"--- report.csv ---\n{csvText}");
        csvText.Split('\n', '\r')[0].Should()
            .Be("identityKey,sourceFolder,destFolder,status,errorCode,updatedAt", "the CSV header must be metadata-only");
        csvText.ToLowerInvariant().Should()
            .NotContain("body").And.NotContain("subject").And.NotContain("sender").And.NotContain("recipient");

        // 10. Security: no plaintext password ever appears across any captured CLI output.
        string allOutput = newOut + fromOut + toOut + preOut + runOut + statusOut + reportOut + csvText;
        allOutput.Should().NotContain(GreenMailCliFixture.SourcePassword)
                 .And.NotContain(GreenMailCliFixture.DestPassword);
    }
}
