using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using EMaigrator.Core.Preflight;   // <-- ADD THIS (ScopeSpec lives here)
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class ConnectTestCommandTests
{
    private static MigrationProfile Profile(ProviderId provider) => new()
    {
        From = new ConnectionProfile { Provider = provider, Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "h", ["accountEmail"] = "a@h" } },
        To = new ConnectionProfile { Provider = provider, Auth = AuthMethod.ImapBasic,
            Settings = new Dictionary<string, string> { ["host"] = "h2", ["accountEmail"] = "a@h2" } },
        Scope = new ScopeSpec { IsBatch = false, Pairs = [] },
    };

    private static (IProviderPlugin plugin, ISourceProvider src) FakePlugin(ProviderId id, ConnectionTestResult result)
    {
        var src = Substitute.For<ISourceProvider>();
        src.TestConnectionAsync(Arg.Any<CancellationToken>()).Returns(result);
        var plugin = Substitute.For<IProviderPlugin>();
        plugin.Id.Returns(id);
        plugin.CreateSource(Arg.Any<ConnectionDescriptor>(), Arg.Any<SecretBundle>()).Returns(src);
        return (plugin, src);
    }

    private static (SecretResolver resolver, ISecretStore store) FakeSecrets()
    {
        var store = Substitute.For<ISecretStore>();
        store.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("ref-x");
        store.RetrieveAsync("ref-x", Arg.Any<CancellationToken>()).Returns("plaintext-pw");
        var reader = Substitute.For<IConsoleSecretReader>();
        reader.ReadSecret(Arg.Any<string>()).Returns("plaintext-pw");
        return (new SecretResolver(store, reader), store);
    }

    [Fact]
    public void BuildDescriptor_mirrors_profile_and_sets_secretRef()
    {
        ConnectionDescriptor d = ConnectionBuilder.BuildDescriptor(Profile(new ProviderId("imap")).From, "ref-x");

        d.Provider.Should().Be(new ProviderId("imap"));
        d.Auth.Should().Be(AuthMethod.ImapBasic);
        d.Settings["host"].Should().Be("h");
        d.SecretRef.Should().Be("ref-x");
    }

    [Fact]
    public async Task Ok_result_returns_success_and_writes_counts()
    {
        var id = new ProviderId("imap");
        var (plugin, _) = FakePlugin(id, new ConnectionTestResult(Ok: true, FolderCount: 7, MessageCount: 99));
        var (resolver, store) = FakeSecrets();
        var sw = new StringWriter();

        CliExitCode code = await ConnectTestCommand.ExecuteAsync(
            Profile(id), MigrationSide.From, [plugin], resolver, store, new HumanOutputWriter(sw), CancellationToken.None);

        code.Should().Be(CliExitCode.Success);
        sw.ToString().Should().Contain("7").And.Contain("99");
        await store.Received(1).PurgeAsync("ref-x", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_result_returns_connection_failed_and_hides_secret()
    {
        var id = new ProviderId("imap");
        var (plugin, _) = FakePlugin(id, new ConnectionTestResult(Ok: false, 0, 0, ErrorCode: "AUTH_FAILED"));
        var (resolver, store) = FakeSecrets();
        var sw = new StringWriter();

        CliExitCode code = await ConnectTestCommand.ExecuteAsync(
            Profile(id), MigrationSide.From, [plugin], resolver, store, new HumanOutputWriter(sw), CancellationToken.None);

        code.Should().Be(CliExitCode.ConnectionFailed);
        sw.ToString().Should().Contain("AUTH_FAILED");
        sw.ToString().Should().NotContain("plaintext-pw");
    }
}
