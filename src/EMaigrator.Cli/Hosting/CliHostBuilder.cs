using EMaigrator.Connectors.Gmail;
using EMaigrator.Connectors.Graph;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Diagnostics;   // IErrorCatalog, ErrorCatalog, ErrorRule
using EMaigrator.Core.Preflight;      // IPreflightAnalyzer, PreflightAnalyzer
using EMaigrator.Infrastructure;
using EMaigrator.Workers;             // AddEmaigratorWorkers
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EMaigrator.Cli.Hosting;

/// <summary>
/// The CLI composition root. Wires Core abstractions to Infrastructure implementations
/// and the connector plugins exactly as the Api/Workers do — the engine is never re-implemented.
/// The in-process single-node worker (AddEmaigratorWorkers) lets `run` actually drain the queue.
/// </summary>
public static class CliHostBuilder
{
    public static IHost Build(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "EMAIGRATOR_");

        // Infrastructure WITHOUT the bus — the in-process worker owns the single MassTransit bus.
        builder.Services.AddInfrastructure(builder.Configuration, registerBus: false);

        // In-process single-node worker: MassTransit bus + consumers + AddWorkerDataSeams
        // (EF connection lookup, IMAP ref-lister/hydrator, EF status writer) + singleton-via-IBus orchestrator.
        builder.Services.AddEmaigratorWorkers(builder.Configuration);

        // Core engine services not registered above (mirror the Api composition).
        builder.Services.AddSingleton<IPreflightAnalyzer, PreflightAnalyzer>();
        builder.Services.AddSingleton<IErrorCatalog>(_ => new ErrorCatalog(new List<ErrorRule>()));

        // Connector plugins (one IProviderPlugin each), per CONTRACTS §8 naming.
        builder.Services.AddImapConnector();
        builder.Services.AddGraphConnector();
        builder.Services.AddGmailConnector();

        // CLI services.
        builder.Services.AddSingleton<Secrets.IConsoleSecretReader, Secrets.ConsoleSecretReader>();
        builder.Services.AddSingleton<Secrets.SecretResolver>();

        // Live CLI seams (run/resume/status): persist Job+MailboxMigration, read MailboxMigration.Status.
        // Singleton: they only consume the singleton IDbContextFactory and are root-resolved by CommandRunner.
        builder.Services.AddSingleton<Commands.IMigrationFactory, Hosting.EfMigrationFactory>();
        builder.Services.AddSingleton<Commands.IMigrationStateReader, Hosting.EfMigrationStateReader>();
        builder.Services.AddSingleton<Commands.IMigrationResetter, Hosting.EfMigrationResetter>();

        // Apply EF migrations at host start so the ledger schema exists before any command runs.
        builder.Services.AddHostedService<Hosting.SchemaMigratorHostedService>();

        return builder.Build();
    }
}
