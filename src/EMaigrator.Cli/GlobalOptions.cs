using System.CommandLine;

namespace EMaigrator.Cli;

public static class GlobalOptions
{
    public static readonly Option<FileInfo?> Profile =
        new("--profile", "-p")
        { Description = "Path to the migration profile JSON file.", Recursive = true };

    public static readonly Option<bool> Json =
        new("--json")
        { Description = "Emit machine-readable JSON to stdout instead of human tables.", Recursive = true };

    public static readonly Option<bool> Verbose =
        new("--verbose", "-v")
        { Description = "Verbose diagnostic logging to stderr.", Recursive = true };
}
