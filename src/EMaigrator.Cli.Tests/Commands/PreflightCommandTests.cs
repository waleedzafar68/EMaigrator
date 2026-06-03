using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Model;
using EMaigrator.Core.Preflight;   // <-- ADD (ScopeSpec, MailboxPair, PreflightPlan, PreflightIssue, MigrationEstimate, IPreflightAnalyzer)
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class PreflightCommandTests
{
    private static MigrationProfile Profile() => new()
    {
        From = new ConnectionProfile { Provider = new ProviderId("imap"), Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "h" } },
        To = new ConnectionProfile { Provider = new ProviderId("imap"), Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "h2" } },
        Scope = new ScopeSpec { IsBatch = false,
            Pairs = [ new MailboxPair("a@h", "a@h2") ] },
    };

    private static IProviderPlugin Plugin()
    {
        var plugin = Substitute.For<IProviderPlugin>();
        plugin.Id.Returns(new ProviderId("imap"));
        plugin.CreateSource(Arg.Any<ConnectionDescriptor>(), Arg.Any<SecretBundle>())
              .Returns(Substitute.For<ISourceProvider>());
        plugin.CreateDestination(Arg.Any<ConnectionDescriptor>(), Arg.Any<SecretBundle>())
              .Returns(Substitute.For<IDestinationProvider>());
        return plugin;
    }

    private static SecretResolver Resolver(out ISecretStore store)
    {
        store = Substitute.For<ISecretStore>();
        store.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("ref-x");
        store.RetrieveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("pw");
        var reader = Substitute.For<IConsoleSecretReader>();
        reader.ReadSecret(Arg.Any<string>()).Returns("pw");
        return new SecretResolver(store, reader);
    }

    private static IPreflightAnalyzer AnalyzerReturning(PreflightPlan plan)
    {
        var a = Substitute.For<IPreflightAnalyzer>();
        a.AnalyzeAsync(Arg.Any<ISourceProvider>(), Arg.Any<IDestinationProvider>(),
                       Arg.Any<ScopeSpec>(), Arg.Any<CancellationToken>()).Returns(plan);
        return a;
    }

    [Fact]
    public async Task No_blockers_returns_success_and_writes_estimate()
    {
        var plan = new PreflightPlan(
            Issues: [ new PreflightIssue("FolderTooDeep", ["/A/B/C/D/E"],
                       RemediationAction.FlattenFolder, [RemediationAction.FlattenFolder],
                       Severity.Warning, "Too deep") ],
            Estimate: new MigrationEstimate(1, 12, 3400, 1_000_000, TimeSpan.FromMinutes(5)));
        var resolver = Resolver(out ISecretStore store);
        var sw = new StringWriter();

        CliExitCode code = await PreflightCommand.ExecuteAsync(
            Profile(), [Plugin()], AnalyzerReturning(plan), resolver, store, new HumanOutputWriter(sw), CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        sw.ToString().Should().Contain("3400").And.Contain("FolderTooDeep");
    }

    [Fact]
    public async Task Blocker_issue_returns_preflight_blocked()
    {
        var plan = new PreflightPlan(
            Issues: [ new PreflightIssue("OverSizeCap", ["Inbox/huge"],
                       RemediationAction.SkipMessage, [RemediationAction.SkipMessage],
                       Severity.Blocker, "Exceeds 50GB cap") ],
            Estimate: new MigrationEstimate(1, 1, 1, 60_000_000_000, TimeSpan.FromHours(2)));
        var resolver = Resolver(out ISecretStore store);

        CliExitCode code = await PreflightCommand.ExecuteAsync(
            Profile(), [Plugin()], AnalyzerReturning(plan), resolver, store,
            new HumanOutputWriter(new StringWriter()), CancellationToken.None);

        code.Should().Be(CliExitCode.PreflightBlocked);
    }

    [Fact]
    public async Task Json_output_has_issues_and_no_secret_keys()
    {
        var plan = new PreflightPlan(
            Issues: [], Estimate: new MigrationEstimate(1, 2, 3, 4, TimeSpan.FromMinutes(1)));
        var resolver = Resolver(out ISecretStore store);
        var sw = new StringWriter();

        await PreflightCommand.ExecuteAsync(
            Profile(), [Plugin()], AnalyzerReturning(plan), resolver, store, new JsonOutputWriter(sw), CancellationToken.None);

        string json = sw.ToString();
        json.Should().Contain("estimate").And.Contain("issues");
        json.ToLowerInvariant().Should().NotContain("password").And.NotContain("\"pw\"");
    }
}
