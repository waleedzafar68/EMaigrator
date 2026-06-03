using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Output;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class CommandRunnerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("emaigrator-runner").FullName;

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SelectWriter_returns_json_writer_when_json_true()
    {
        IOutputWriter writer = CommandRunner.SelectWriter(json: true, new StringWriter());
        writer.Should().BeOfType<JsonOutputWriter>();
    }

    [Fact]
    public void SelectWriter_returns_human_writer_when_json_false()
    {
        IOutputWriter writer = CommandRunner.SelectWriter(json: false, new StringWriter());
        writer.Should().BeOfType<HumanOutputWriter>();
    }

    [Fact]
    public void ResolveProfile_returns_config_error_when_option_null()
    {
        var result = CommandRunner.ResolveProfile(profilePath: null);
        result.Ok.Should().BeFalse();
        result.ExitCode.Should().Be(CliExitCode.ConfigError);
    }

    [Fact]
    public void ResolveProfile_loads_existing_valid_profile()
    {
        string path = Path.Combine(_dir, "p.json");
        File.WriteAllText(path, """
        { "from": { "provider": "imap", "auth": "ImapBasic", "settings": { "host": "h" } },
          "to":   { "provider": "imap", "auth": "ImapBasic", "settings": { "host": "h2" } },
          "scope": { "isBatch": false, "pairs": [] } }
        """);

        var result = CommandRunner.ResolveProfile(path);

        result.Ok.Should().BeTrue();
    }
}
