using EMaigrator.Cli;
using EMaigrator.Cli.Profile;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Model;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Profile;

public class ProfileLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("emaigrator-profile").FullName;
    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string WriteProfile(string json)
    {
        string path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Loads_a_valid_profile()
    {
        string path = WriteProfile("""
        {
          "tenantId": "self-host",
          "storeSubjects": false,
          "from": { "provider": "imap", "auth": "ImapBasic",
                    "settings": { "host": "src.example.com", "port": "993", "accountEmail": "a@src.example.com" } },
          "to":   { "provider": "imap", "auth": "ImapBasic",
                    "settings": { "host": "dst.example.com", "port": "993", "accountEmail": "a@dst.example.com" } },
          "scope": { "isBatch": false, "pairs": [ { "sourceMailbox": "a@src.example.com", "destMailbox": "a@dst.example.com" } ] }
        }
        """);

        ProfileLoadResult result = ProfileLoader.Load(path);

        result.Ok.Should().BeTrue();
        result.Profile!.From.Provider.Should().Be(new ProviderId("imap"));
        result.Profile.From.Auth.Should().Be(AuthMethod.ImapBasic);
        result.Profile.To.Settings["host"].Should().Be("dst.example.com");
        result.Profile.Scope.Pairs.Should().ContainSingle()
            .Which.SourceMailbox.Should().Be("a@src.example.com");
        result.Profile.TenantId.Should().Be("self-host");
    }

    [Fact]
    public void Missing_file_is_config_error()
    {
        ProfileLoadResult result = ProfileLoader.Load(Path.Combine(_dir, "nope.json"));

        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(CliExitCode.ConfigError);
        result.Error.Should().Contain("nope.json");
    }

    [Fact]
    public void Malformed_json_is_config_error()
    {
        string path = WriteProfile("{ this is not json ");

        ProfileLoadResult result = ProfileLoader.Load(path);

        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(CliExitCode.ConfigError);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("Secret")]
    [InlineData("apiKey")]
    [InlineData("clientCredential")]
    public void Plaintext_secret_in_settings_is_rejected(string secretKey)
    {
        string path = WriteProfile($$"""
        {
          "from": { "provider": "imap", "auth": "ImapBasic",
                    "settings": { "host": "src.example.com", "{{secretKey}}": "hunter2" } },
          "to":   { "provider": "imap", "auth": "ImapBasic", "settings": { "host": "dst.example.com" } },
          "scope": { "isBatch": false, "pairs": [] }
        }
        """);

        ProfileLoadResult result = ProfileLoader.Load(path);

        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(CliExitCode.ConfigError);
        result.Error.Should().Contain("env").And.Contain("prompt");
    }
}
