using EMaigrator.Connectors.Gmail;
using EMaigrator.Connectors.Graph;
using EMaigrator.Connectors.Imap;
using EMaigrator.Core.Diagnostics;   // IErrorCatalog, ErrorCatalog, ErrorRule
using EMaigrator.Core.Preflight;      // IPreflightAnalyzer, PreflightAnalyzer
using EMaigrator.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EMaigrator.Cli.Hosting;

/// <summary>
/// The CLI composition root. Wires Core abstractions to Infrastructure implementations
/// and the connector plugins exactly as the Api/Workers do — the engine is never re-implemented.
/// The in-process single-node worker (AddEmaigratorWorkers) is wired in Task 12 for `run`.
/// </summary>
public static class CliHostBuilder
{
    public static IHost Build(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "EMAIGRATOR_");

        // Infrastructure: EF/Postgres ledger, ISecretStore, Redis rate limiter, IJobOrchestrator, etc.
        builder.Services.AddInfrastructure(builder.Configuration);

        // Core engine services not registered by AddInfrastructure (mirror the Api composition).
        builder.Services.AddSingleton<IPreflightAnalyzer, PreflightAnalyzer>();
        builder.Services.AddSingleton<IErrorCatalog>(_ => new ErrorCatalog(new List<ErrorRule>()));

        // Connector plugins (one IProviderPlugin each), per CONTRACTS §8 naming.
        builder.Services.AddImapConnector();
        builder.Services.AddGraphConnector();
        builder.Services.AddGmailConnector();

        // CLI services.
        builder.Services.AddSingleton<Secrets.IConsoleSecretReader, Secrets.ConsoleSecretReader>();
        builder.Services.AddSingleton<Secrets.SecretResolver>();

        return builder.Build();
    }
}
