namespace EMaigrator.Cli.Profile;

public sealed record ProfileLoadResult
{
    public bool Ok { get; private init; }
    public MigrationProfile? Profile { get; private init; }
    public string? Error { get; private init; }
    public CliExitCode ExitCode { get; private init; }

    public static ProfileLoadResult Success(MigrationProfile profile) =>
        new() { Ok = true, Profile = profile, ExitCode = CliExitCode.Success };

    public static ProfileLoadResult Failed(string error) =>
        new() { Ok = false, Error = error, ExitCode = CliExitCode.ConfigError };
}
