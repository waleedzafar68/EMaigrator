using System.CommandLine;
using EMaigrator.Cli.Hosting;
using EMaigrator.Cli.Output;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Preflight;   // <-- ADDED: IPreflightAnalyzer lives here
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EMaigrator.Cli.Commands;

public static class CommandRunner
{
    public static IOutputWriter SelectWriter(bool json, TextWriter output) =>
        json ? new JsonOutputWriter(output) : new HumanOutputWriter(output);

    public static ProfileLoadResult ResolveProfile(string? profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
            return ProfileLoadResult.Failed("No --profile specified. Pass --profile <path>.");
        return ProfileLoader.Load(profilePath);
    }

    private static (IHost host, MigrationProfile profile, IOutputWriter writer, CliExitCode? earlyExit)
        Bootstrap(ParseResult parse)
    {
        bool json = parse.GetValue(GlobalOptions.Json);
        FileInfo? profileFile = parse.GetValue(GlobalOptions.Profile);
        IOutputWriter writer = SelectWriter(json, Console.Out);

        ProfileLoadResult loaded = ResolveProfile(profileFile?.FullName);
        if (!loaded.Ok)
        {
            writer.WriteError(loaded.Error!);
            return (null!, null!, writer, loaded.ExitCode);
        }

        IHost host = CliHostBuilder.Build([]);
        return (host, loaded.Profile!, writer, null);
    }

    public static async Task<int> RunConnectTestAsync(ParseResult parse, Option<string> sideOpt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parse);
        var (host, profile, writer, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            var plugins = host.Services.GetServices<IProviderPlugin>().ToList();
            var resolver = host.Services.GetRequiredService<SecretResolver>();
            var store = host.Services.GetRequiredService<ISecretStore>();
            MigrationSide side = parse.GetValue(sideOpt) == "to" ? MigrationSide.To : MigrationSide.From;
            return (int)await ConnectTestCommand.ExecuteAsync(profile, side, plugins, resolver, store, writer, ct);
        }
        finally { await host.StopAsync(ct); }
    }

    public static async Task<int> RunPreflightAsync(ParseResult parse, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parse);
        var (host, profile, writer, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            var plugins = host.Services.GetServices<IProviderPlugin>().ToList();
            return (int)await PreflightCommand.ExecuteAsync(
                profile, plugins,
                host.Services.GetRequiredService<IPreflightAnalyzer>(),
                host.Services.GetRequiredService<SecretResolver>(),
                host.Services.GetRequiredService<ISecretStore>(), writer, ct);
        }
        finally { await host.StopAsync(ct); }
    }

    public static async Task<int> RunMigrationAsync(ParseResult parse, Option<Guid?> idOpt, bool resume, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parse);
        var (host, profile, writer, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            Guid? supplied = parse.GetValue(idOpt);
            Guid id = supplied ?? await host.Services.GetRequiredService<IMigrationFactory>().CreateAsync(profile, ct);
            return (int)await RunCommand.ExecuteAsync(
                id, host.Services.GetRequiredService<IJobOrchestrator>(),
                host.Services.GetRequiredService<IMigrationStateReader>(),
                host.Services.GetRequiredService<ILedger>(), writer, resume, ct);
        }
        finally { await host.StopAsync(ct); }
    }

    public static async Task<int> RunMigrationAsync(ParseResult parse, Option<Guid> idOpt, bool resume, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parse);
        var (host, _, writer, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            Guid id = parse.GetValue(idOpt);
            return (int)await RunCommand.ExecuteAsync(
                id, host.Services.GetRequiredService<IJobOrchestrator>(),
                host.Services.GetRequiredService<IMigrationStateReader>(),
                host.Services.GetRequiredService<ILedger>(), writer, resume, ct);
        }
        finally { await host.StopAsync(ct); }
    }

    public static async Task<int> RunStatusAsync(ParseResult parse, Option<Guid> idOpt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parse);
        var (host, _, writer, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            return (int)await StatusCommand.ExecuteAsync(
                parse.GetValue(idOpt),
                host.Services.GetRequiredService<IMigrationStateReader>(),
                host.Services.GetRequiredService<ILedger>(), writer, ct);
        }
        finally { await host.StopAsync(ct); }
    }

    public static async Task<int> RunReportAsync(ParseResult parse, Option<Guid> idOpt, Option<FileInfo?> outOpt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parse);
        var (host, _, _, early) = Bootstrap(parse);
        if (early is { } e) return (int)e;
        await host.StartAsync(ct);
        try
        {
            FileInfo? outFile = parse.GetValue(outOpt);
            TextWriter csv = outFile is null ? Console.Out : new StreamWriter(outFile.FullName, append: false);
            try
            {
                return (int)await ReportCommand.ExecuteAsync(
                    parse.GetValue(idOpt), host.Services.GetRequiredService<ILedger>(), csv, ct);
            }
            finally { if (outFile is not null) await csv.DisposeAsync(); }
        }
        finally { await host.StopAsync(ct); }
    }
}
