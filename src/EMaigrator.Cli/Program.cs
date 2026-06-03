using System.CommandLine;
using System.Reflection;

namespace EMaigrator.Cli;

public static class CommandFactory
{
    public static RootCommand BuildRootCommand()
    {
        RootCommand root = new("emaigrator — non-destructive, idempotent, resumable email migration.");
        SetRootCommandName(root, "emaigrator");
        root.Options.Add(GlobalOptions.Profile);
        root.Options.Add(GlobalOptions.Json);
        root.Options.Add(GlobalOptions.Verbose);
        return root;
    }

    // System.CommandLine 2.0.0-beta5 makes Symbol.Name get-only and defaults a RootCommand's name
    // to the running executable (which is "emaigrator" for the published binary, but the test host
    // otherwise). Pin the name explicitly so help text and the parsed command identity are stable
    // regardless of the host process. The backing field is the only seam beta5 exposes for this.
    private static readonly FieldInfo NameBackingField =
        typeof(Symbol).GetField("<Name>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("System.CommandLine Symbol.Name backing field not found; the package version may have changed.");

    private static void SetRootCommandName(RootCommand root, string name) =>
        NameBackingField.SetValue(root, name);
}

public static class Program
{
    public static int Main(string[] args)
    {
        RootCommand root = CommandFactory.BuildRootCommand();
        return root.Parse(args).Invoke();
    }
}
