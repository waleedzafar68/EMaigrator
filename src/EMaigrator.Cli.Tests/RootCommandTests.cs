using System.CommandLine;
using EMaigrator.Cli;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests;

public class RootCommandTests
{
    [Fact]
    public void Root_command_is_named_emaigrator_and_has_global_options()
    {
        RootCommand root = CommandFactory.BuildRootCommand();

        root.Name.Should().Be("emaigrator");
        root.Options.Should().Contain(o => o.Name == "--profile");
        root.Options.Should().Contain(o => o.Name == "--json");
        root.Options.Should().Contain(o => o.Name == "--verbose");
    }

    [Fact]
    public void Help_invocation_returns_success_exit_code()
    {
        RootCommand root = CommandFactory.BuildRootCommand();

        int exit = root.Parse("--help").Invoke();

        exit.Should().Be((int)CliExitCode.Success);
    }

    [Fact]
    public void Exit_codes_have_the_frozen_numeric_values()
    {
        ((int)CliExitCode.Success).Should().Be(0);
        ((int)CliExitCode.UsageError).Should().Be(2);
        ((int)CliExitCode.ConnectionFailed).Should().Be(3);
        ((int)CliExitCode.PreflightBlocked).Should().Be(4);
        ((int)CliExitCode.MigrationFailed).Should().Be(5);
        ((int)CliExitCode.MigrationPartial).Should().Be(6);
        ((int)CliExitCode.ConfigError).Should().Be(7);
        ((int)CliExitCode.Cancelled).Should().Be(130);
    }
}
