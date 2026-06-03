using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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

namespace EMaigrator.Cli.IntegrationTests.Security;

/// <summary>
/// USER-GATE Security Verification (Plan 09 Task 14, NON-SKIPPABLE): proves the CLI's
/// credential-handling story against the real GreenMail + Postgres + RabbitMQ + Redis stack —
/// (1) no secret-bearing CLI option exists anywhere in the command tree; (2) a password supplied
/// ONLY via <c>EMAIGRATOR_SECRET_FROM</c> never appears in captured stdout/stderr; (3) <c>migration
/// new</c> writes an owner-only profile file (POSIX mode 600 / Windows ACL with no group/other
/// identities); (4) <c>--json</c> output (preflight + connect-test) excludes secret keys and secret
/// values under a recursive walk. The shared <see cref="GreenMailCliFixture"/> already sets every
/// EMAIGRATOR_-prefixed environment variable (including SECRET_FROM/_TO), so this test sets none of
/// them — it relies on that env exactly as the operator-happy-path siblings do.
/// </summary>
[Collection("cli-e2e")]
public sealed class CredentialHandlingSecurityTests : IDisposable
{
    private const int SeedCount = 5;

    private readonly GreenMailCliFixture _fx;
    private readonly ITestOutputHelper _log;
    private readonly string _dir;

    public CredentialHandlingSecurityTests(GreenMailCliFixture fx, ITestOutputHelper log)
    {
        _fx = fx;
        _log = log;
        _dir = Directory.CreateTempSubdirectory("emaigrator-cli-sec").FullName;
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
            msg.Subject = $"Sec {i}";
            msg.MessageId = $"<sec-{i}@greenmail.local>";
            msg.Body = new TextPart("plain") { Text = $"Body {i}" };
            await inbox.AppendAsync(new AppendRequest(msg, MessageFlags.Seen, DateTimeOffset.UtcNow));
        }

        await client.DisconnectAsync(true);
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

    // --- Acceptance #1: no secret-bearing option anywhere in the command tree. -----------------

    [Fact]
    public void No_secret_bearing_option_exists_anywhere_in_command_tree()
    {
        string[] forbidden = ["password", "secret", "token", "credential", "apikey"];
        var offenders = new List<string>();

        void Walk(Command cmd, string prefix)
        {
            foreach (Option opt in cmd.Options)
            {
                string n = opt.Name.ToLowerInvariant();
                if (forbidden.Any(f => n.Contains(f, StringComparison.Ordinal)))
                    offenders.Add($"{prefix} {opt.Name}");
            }

            foreach (Command sub in cmd.Subcommands)
                Walk(sub, $"{prefix} {sub.Name}");
        }

        Walk(CommandFactory.BuildRootCommand(), "emaigrator");

        _log.WriteLine($"option-walk offenders: [{string.Join(", ", offenders)}] (count {offenders.Count})");
        offenders.Should().BeEmpty("the CLI must never accept a secret as a command-line argument");
    }

    // --- Acceptance #2: env-provided secret never appears in captured output. ------------------

    [Fact]
    public async Task Env_secret_never_appears_in_captured_connect_test_output()
    {
        await SeedSourceAsync();
        string profile = CliProfiles.WriteImapToImap(_dir, _fx.ImapPort);

        // The password reaches the connector ONLY via EMAIGRATOR_SECRET_FROM (set by the fixture).
        (int exit, string output) =
            await InvokeCliAsync(["connect", "test", "--side", "from", "--profile", profile, "--json"]);

        _log.WriteLine($"--- connect test from (exit {exit}) ---\n{output}");
        exit.Should().Be((int)CliExitCode.Success, "connect test --side from should succeed; output:\n{0}", output);

        // (a) The literal env-provided password must never appear ANYWHERE in the captured stream
        //     (stdout + stderr + interleaved host logging). This is the load-bearing leak check.
        int occurrences = CountOccurrences(output, GreenMailCliFixture.SourcePassword);
        _log.WriteLine($"password-occurrence count in connect-test output = {occurrences}");
        occurrences.Should().Be(0, "the env-provided password must never be echoed to stdout/stderr");

        // (b) No opaque secret REFERENCE may surface in the command's user-facing JSON. The capture
        //     also carries the CLI host's EF-Core SQL command logging, which legitimately names the
        //     `credentials.SecretRef` *column* (parameter values are masked as '?', never the ref
        //     value). So scope the secret-ref/secret-key check to the writer's actual JSON object —
        //     exactly where a real leak would land — rather than the infrastructure log noise.
        string json = ExtractLastJsonObject(output);
        _log.WriteLine($"--- connect-test command JSON ---\n{json}");
        json.ToLowerInvariant().Should().NotContain("secretref",
            "the command's JSON output must never carry an opaque secret reference");
        AssertNoSecret(JsonDocument.Parse(json).RootElement,
            [GreenMailCliFixture.SourcePassword, GreenMailCliFixture.DestPassword]);
    }

    // --- Acceptance #3: migration new writes an owner-only profile file. -----------------------

    [Fact]
    public void Migration_new_writes_owner_only_profile_file()
    {
        string path = Path.Combine(_dir, "p.json");

        int exit = CommandFactory.BuildRootCommand().Parse(["migration", "new", "--profile", path]).Invoke();
        exit.Should().Be((int)CliExitCode.Success, "migration new should succeed");
        File.Exists(path).Should().BeTrue("migration new should have written the profile file");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            UnixFileMode groupOther =
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            _log.WriteLine($"POSIX file mode = {mode}");
            (mode & groupOther).Should().Be(UnixFileMode.None, "group/other must have no access (mode 600)");
            (mode & UnixFileMode.UserRead).Should().Be(UnixFileMode.UserRead);
            (mode & UnixFileMode.UserWrite).Should().Be(UnixFileMode.UserWrite);
        }
        else
        {
            AssertWindowsOwnerOnly(path);
        }
    }

    [SupportedOSPlatform("windows")]
    private void AssertWindowsOwnerOnly(string path)
    {
        var sec = new FileInfo(path).GetAccessControl();
        var rules = sec.GetAccessRules(
            includeExplicit: true, includeInherited: true,
            typeof(System.Security.Principal.NTAccount));

        var identities = new List<string>();
        foreach (System.Security.AccessControl.FileSystemAccessRule r in rules)
        {
            string id = r.IdentityReference.Value;
            identities.Add(id);
            string lower = id.ToLowerInvariant();
            lower.Should().NotContain("everyone")
                 .And.NotContain("users")
                 .And.NotContain("authenticated");
        }

        _log.WriteLine($"Windows ACL identities = [{string.Join(", ", identities)}]");
    }

    // --- Acceptance #4: --json output excludes secrets under a recursive walk. ------------------

    [Fact]
    public async Task Json_output_contains_no_secret_keys_or_values()
    {
        await SeedSourceAsync();
        string profile = CliProfiles.WriteImapToImap(_dir, _fx.ImapPort);
        string[] secrets = [GreenMailCliFixture.SourcePassword, GreenMailCliFixture.DestPassword];

        (int preExit, string preOut) = await InvokeCliAsync(["preflight", "--profile", profile, "--json"]);
        _log.WriteLine($"--- preflight (exit {preExit}) ---\n{preOut}");
        preExit.Should().Be((int)CliExitCode.Success, "preflight should succeed; output:\n{0}", preOut);

        (int connExit, string connOut) =
            await InvokeCliAsync(["connect", "test", "--side", "from", "--profile", profile, "--json"]);
        _log.WriteLine($"--- connect test from (exit {connExit}) ---\n{connOut}");
        connExit.Should().Be((int)CliExitCode.Success, "connect test --side from should succeed; output:\n{0}", connOut);

        AssertJsonHasNoSecret(preOut, secrets);
        AssertJsonHasNoSecret(connOut, secrets);
    }

    private static void AssertJsonHasNoSecret(string output, string[] secrets)
    {
        string json = ExtractLastJsonObject(output);
        using var doc = JsonDocument.Parse(json);
        // A well-formed JSON object is expected on success; the { "error": ... } shape is also a valid
        // object and is walked identically (its name/values must equally be secret-free).
        AssertNoSecret(doc.RootElement, secrets);
    }

    /// <summary>
    /// Pulls the CLI writer's emitted JSON object out of a captured console stream that also carries
    /// the host's structured logging. The writer (<c>JsonOutputWriter</c>, indented camelCase) emits a
    /// single balanced object as the final JSON in the stream; we scan from the last <c>}</c> back to
    /// its brace-balanced <c>{</c> so interleaved log lines (which use <c>[...]</c>, not <c>{...}</c>)
    /// cannot corrupt the slice.
    /// </summary>
    private static string ExtractLastJsonObject(string output)
    {
        int end = output.LastIndexOf('}');
        end.Should().BeGreaterThanOrEqualTo(0, "--json output should contain a closed JSON object; got:\n{0}", output);

        int depth = 0;
        for (int i = end; i >= 0; i--)
        {
            if (output[i] == '}') depth++;
            else if (output[i] == '{' && --depth == 0)
                return output[i..(end + 1)];
        }

        throw new Xunit.Sdk.XunitException($"--json output had no balanced JSON object; got:\n{output}");
    }

    private static void AssertNoSecret(JsonElement el, string[] secrets)
    {
        string[] forbiddenKeys = ["secret", "password", "token", "secretref", "credential"];
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty p in el.EnumerateObject())
                {
                    string key = p.Name.ToLowerInvariant();
                    forbiddenKeys.Should().NotContain(
                        k => key.Contains(k, StringComparison.Ordinal),
                        "JSON property name '{0}' must not look like a secret", p.Name);
                    AssertNoSecret(p.Value, secrets);
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in el.EnumerateArray())
                    AssertNoSecret(item, secrets);
                break;
            case JsonValueKind.String:
                string s = el.GetString() ?? string.Empty;
                secrets.Should().NotContain(
                    secret => s.Contains(secret, StringComparison.Ordinal),
                    "a JSON string value must never equal or contain a seeded password");
                break;
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }

        return count;
    }
}
