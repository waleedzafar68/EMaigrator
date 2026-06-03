using System.CommandLine;
using EMaigrator.Cli;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Parsing;

public class CommandParsingTests
{
    private static ParseResult Parse(string args) =>
        CommandFactory.BuildRootCommand().Parse(args);

    [Fact]
    public void Migration_new_parses_clean()
    {
        ParseResult r = Parse("migration new --profile x.json");
        r.Errors.Should().BeEmpty();
        r.CommandResult.Command.Name.Should().Be("new");
    }

    [Fact]
    public void Connect_test_rejects_invalid_side()
    {
        ParseResult r = Parse("connect test --side sideways");
        r.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Connect_test_accepts_from()
    {
        ParseResult r = Parse("connect test --side from");
        r.Errors.Should().BeEmpty();
        r.CommandResult.Command.Name.Should().Be("test");
    }

    [Theory]
    [InlineData("preflight", "preflight")]
    [InlineData("run", "run")]
    public void Top_level_commands_parse_clean(string args, string expectedName)
    {
        ParseResult r = Parse(args);
        r.Errors.Should().BeEmpty();
        r.CommandResult.Command.Name.Should().Be(expectedName);
    }

    [Fact]
    public void Resume_requires_id()
    {
        Parse("resume").Errors.Should().NotBeEmpty();
        Parse($"resume --id {Guid.NewGuid()}").Errors.Should().BeEmpty();
    }

    [Fact]
    public void Status_and_report_require_id()
    {
        Parse("status").Errors.Should().NotBeEmpty();
        Parse("report").Errors.Should().NotBeEmpty();
        Parse($"status --id {Guid.NewGuid()}").Errors.Should().BeEmpty();
        Parse($"report --id {Guid.NewGuid()}").Errors.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_command_is_a_parse_error_and_nonzero_exit()
    {
        ParseResult r = Parse("bogus");
        r.Errors.Should().NotBeEmpty();
        r.Invoke().Should().NotBe((int)CliExitCode.Success);
    }

    [Fact]
    public void No_command_exposes_a_password_or_secret_option_anywhere()
    {
        // Defense in depth against accidentally adding a secret-bearing flag.
        RootCommand root = CommandFactory.BuildRootCommand();
        AssertNoSecretOption(root);

        static void AssertNoSecretOption(Command cmd)
        {
            foreach (Option o in cmd.Options)
            {
                o.Name.Should().NotContainEquivalentOf("password")
                    .And.NotContainEquivalentOf("secret")
                    .And.NotContainEquivalentOf("token");
            }
            foreach (Command sub in cmd.Subcommands) AssertNoSecretOption(sub);
        }
    }
}
