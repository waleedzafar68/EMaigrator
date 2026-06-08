using System.CommandLine;
using System.Reflection;
using EMaigrator.Cli.Commands;

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
        root.Subcommands.Add(BuildMigrationCommand());
        root.Subcommands.Add(BuildConnectCommand());
        root.Subcommands.Add(BuildPreflightCommand());
        root.Subcommands.Add(BuildRunCommand());
        root.Subcommands.Add(BuildResumeCommand());
        root.Subcommands.Add(BuildReconcileCommand());
        root.Subcommands.Add(BuildStatusCommand());
        root.Subcommands.Add(BuildReportCommand());
        return root;
    }

    private static Command BuildMigrationCommand()
    {
        var migration = new Command("migration", "Manage migration profiles.");

        var newCmd = new Command("new", "Scaffold a starter migration profile file.");
        // Reuse the recursive global --profile; only --force is local to `migration new`.
        var forceOpt = new Option<bool>("--force") { Description = "Overwrite an existing file." };
        newCmd.Options.Add(forceOpt);
        newCmd.SetAction(parse =>
        {
            FileInfo? target = parse.GetValue(GlobalOptions.Profile);
            if (target is null)
            {
                Console.Error.WriteLine("migration new requires --profile <path> (where to write the profile).");
                return (int)CliExitCode.ConfigError;
            }
            return (int)MigrationNewCommand.Execute(target.FullName, parse.GetValue(forceOpt));
        });

        migration.Subcommands.Add(newCmd);
        return migration;
    }

    private static Command BuildConnectCommand()
    {
        var connect = new Command("connect", "Test provider connections.");
        var test = new Command("test", "Test a side's connection (fail fast before migrating).");
        var sideOpt = new Option<string>("--side")
        { Description = "Which side to test: from|to.", Required = true };
        sideOpt.AcceptOnlyFromAmong("from", "to");
        test.Options.Add(sideOpt);
        test.SetAction((parse, ct) =>
            CommandRunner.RunConnectTestAsync(parse, sideOpt, ct));
        connect.Subcommands.Add(test);
        return connect;
    }

    private static Command BuildRunCommand()
    {
        var run = new Command("run", "Run the migration to completion (self-host in-process worker).");
        var idOpt = new Option<Guid?>("--id")
        { Description = "Existing mailbox-migration id; omit to create from the profile." };
        run.Options.Add(idOpt);
        run.SetAction((parse, ct) => CommandRunner.RunMigrationAsync(parse, idOpt, resume: false, ct));
        return run;
    }

    private static Command BuildResumeCommand()
    {
        var resume = new Command("resume", "Re-enqueue not-done items for an existing migration.");
        var idOpt = new Option<Guid>("--id")
        { Description = "Existing mailbox-migration id to resume.", Required = true };
        resume.Options.Add(idOpt);
        resume.SetAction((parse, ct) => CommandRunner.RunMigrationAsync(parse, idOpt, resume: true, ct));
        return resume;
    }

    private static Command BuildReconcileCommand()
    {
        var reconcile = new Command("reconcile",
            "Reconcile an existing migration against the live destination (copy missing, backfill attachments).");
        var idOpt = new Option<Guid>("--id")
        { Description = "Existing mailbox-migration id to reconcile.", Required = true };
        var matchOpt = new Option<string?>("--match")
        { Description = "Match strictness: metadata (default) | hash." };
        matchOpt.AcceptOnlyFromAmong("metadata", "hash");
        reconcile.Options.Add(idOpt);
        reconcile.Options.Add(matchOpt);
        reconcile.SetAction((parse, ct) => CommandRunner.RunReconcileAsync(parse, idOpt, matchOpt, ct));
        return reconcile;
    }

    private static Command BuildStatusCommand()
    {
        var status = new Command("status", "Show a migration's current status and counts.");
        var idOpt = new Option<Guid>("--id") { Description = "Mailbox-migration id.", Required = true };
        status.Options.Add(idOpt);
        status.SetAction((parse, ct) => CommandRunner.RunStatusAsync(parse, idOpt, ct));
        return status;
    }

    private static Command BuildReportCommand()
    {
        var report = new Command("report", "Export a metadata-only CSV report of ledger entries.");
        var idOpt = new Option<Guid>("--id") { Description = "Mailbox-migration id.", Required = true };
        var outOpt = new Option<FileInfo?>("--out", "-o") { Description = "CSV file (default: stdout)." };
        report.Options.Add(idOpt);
        report.Options.Add(outOpt);
        report.SetAction((parse, ct) => CommandRunner.RunReportAsync(parse, idOpt, outOpt, ct));
        return report;
    }

    // System.CommandLine 2.0.0-beta5 makes Symbol.Name get-only and defaults a RootCommand's name
    // to the running executable (which is "emaigrator" for the published binary, but the test host
    // otherwise). Pin the name explicitly so help text and the parsed command identity are stable
    // regardless of the host process. The backing field is the only seam beta5 exposes for this.
    private static readonly FieldInfo NameBackingField =
        typeof(Symbol).GetField("<Name>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("System.CommandLine Symbol.Name backing field not found; the package version may have changed.");

    private static Command BuildPreflightCommand()
    {
        var preflight = new Command("preflight",
            "Read-only scan: enumerate issues + estimate, gate before running.");
        preflight.SetAction((parse, ct) => CommandRunner.RunPreflightAsync(parse, ct));
        return preflight;
    }

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
