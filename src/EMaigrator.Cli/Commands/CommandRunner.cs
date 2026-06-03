using System.CommandLine;

namespace EMaigrator.Cli.Commands;

/// <summary>
/// Glue between System.CommandLine actions and the command implementations.
/// TEMPORARY stubs for Tasks 6-9; the live host-wiring implementation lands in Task 10.
/// </summary>
public static class CommandRunner
{
    public static Task<int> RunConnectTestAsync(ParseResult parse, Option<string> sideOpt, CancellationToken ct)
    {
        _ = parse;
        _ = sideOpt;
        _ = ct;
        return Task.FromResult((int)CliExitCode.UsageError);
    }

    public static Task<int> RunPreflightAsync(ParseResult parse, CancellationToken ct) =>
        Task.FromResult((int)CliExitCode.UsageError);
}
