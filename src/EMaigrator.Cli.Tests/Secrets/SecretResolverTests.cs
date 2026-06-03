using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EMaigrator.Cli.Tests.Secrets;

public class SecretResolverTests
{
    private static ConnectionProfile Conn(AuthMethod auth = AuthMethod.ImapBasic) => new()
    {
        Provider = new ProviderId("imap"),
        Auth = auth,
        Settings = new Dictionary<string, string> { ["host"] = "h", ["accountEmail"] = "a@h" },
    };

    private static ISecretStore StoreReturning(string secretRef)
    {
        ISecretStore store = Substitute.For<ISecretStore>();
        store.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(secretRef);
        return store;
    }

    [Fact]
    public async Task Reads_from_env_var_when_present_and_never_prompts()
    {
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", "env-password");
        try
        {
            ISecretStore store = StoreReturning("ref-123");
            IConsoleSecretReader reader = Substitute.For<IConsoleSecretReader>();
            var resolver = new SecretResolver(store, reader);

            string secretRef = await resolver.ResolveAsync(
                MigrationSide.From, Conn(), tenantId: "t1", CancellationToken.None);

            secretRef.Should().Be("ref-123");
            await store.Received(1).StoreAsync("t1",
                Arg.Is<string>(s => s.Contains("env-password") && s.Contains("password")),
                Arg.Any<CancellationToken>());
            reader.DidNotReceiveWithAnyArgs().ReadSecret(default!);
        }
        finally { Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", null); }
    }

    [Fact]
    public async Task Falls_back_to_no_echo_prompt_when_env_missing()
    {
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_TO", null);
        ISecretStore store = StoreReturning("ref-prompted");
        IConsoleSecretReader reader = Substitute.For<IConsoleSecretReader>();
        reader.ReadSecret(Arg.Any<string>()).Returns("typed-password");
        var resolver = new SecretResolver(store, reader);

        string secretRef = await resolver.ResolveAsync(
            MigrationSide.To, Conn(), tenantId: "t1", CancellationToken.None);

        secretRef.Should().Be("ref-prompted");
        await store.Received(1).StoreAsync("t1",
            Arg.Is<string>(s => s.Contains("typed-password")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Service_account_env_value_is_treated_as_file_path_and_contents_are_stored()
    {
        string saPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(saPath, "{\"type\":\"service_account\",\"private_key\":\"PK\"}");
        Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", saPath);
        try
        {
            ISecretStore store = StoreReturning("ref-sa");
            var resolver = new SecretResolver(store, Substitute.For<IConsoleSecretReader>());

            await resolver.ResolveAsync(
                MigrationSide.From, Conn(AuthMethod.GmailServiceAccountDwd), "t1", CancellationToken.None);

            await store.Received(1).StoreAsync("t1",
                Arg.Is<string>(s => s.Contains("private_key") && s.Contains("PK")),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("EMAIGRATOR_SECRET_FROM", null);
            File.Delete(saPath);
        }
    }
}
