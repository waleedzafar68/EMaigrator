using EMaigrator.Cli;
using EMaigrator.Cli.Commands;
using EMaigrator.Cli.Profile;
using FluentAssertions;
using Xunit;

namespace EMaigrator.Cli.Tests.Commands;

public class MigrationNewCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("emaigrator-new").FullName;

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Creates_a_loadable_starter_profile_with_no_secret_keys()
    {
        string path = Path.Combine(_dir, "profile.json");

        CliExitCode code = MigrationNewCommand.Execute(path, force: false);

        code.Should().Be(CliExitCode.Success);
        ProfileLoadResult loaded = ProfileLoader.Load(path);
        loaded.Ok.Should().BeTrue("generated profile must round-trip through the loader (no plaintext secrets)");
    }

    [Fact]
    public void Refuses_to_overwrite_without_force()
    {
        string path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, "existing");

        CliExitCode code = MigrationNewCommand.Execute(path, force: false);

        code.Should().Be(CliExitCode.ConfigError);
        File.ReadAllText(path).Should().Be("existing");
    }

    [Fact]
    public void Overwrites_with_force()
    {
        string path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, "existing");

        CliExitCode code = MigrationNewCommand.Execute(path, force: true);

        code.Should().Be(CliExitCode.Success);
        ProfileLoader.Load(path).Ok.Should().BeTrue();
    }
}
