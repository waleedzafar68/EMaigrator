using System.Text.Json;
using EMaigrator.Cli.Output;
using EMaigrator.Cli.Profile;
using EMaigrator.Cli.Secrets;
using EMaigrator.Core.Abstractions;
using EMaigrator.Core.Diagnostics;
using EMaigrator.Core.Preflight;   // PreflightPlan, IPreflightAnalyzer, ScopeSpec, PreflightIssue, MigrationEstimate

namespace EMaigrator.Cli.Commands;

public static class PreflightCommand
{
    public static async Task<CliExitCode> ExecuteAsync(
        MigrationProfile profile, IReadOnlyList<IProviderPlugin> plugins,
        IPreflightAnalyzer analyzer, SecretResolver secretResolver, ISecretStore secretStore,
        IOutputWriter writer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(secretResolver);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(writer);

        IProviderPlugin? fromPlugin = plugins.FirstOrDefault(p => p.Id.Equals(profile.From.Provider));
        IProviderPlugin? toPlugin = plugins.FirstOrDefault(p => p.Id.Equals(profile.To.Provider));
        if (fromPlugin is null || toPlugin is null)
        {
            writer.WriteError("Missing connector plugin for source or destination provider.");
            return CliExitCode.ConfigError;
        }

        string fromRef = await secretResolver.ResolveAsync(MigrationSide.From, profile.From, profile.TenantId, ct);
        string toRef = await secretResolver.ResolveAsync(MigrationSide.To, profile.To, profile.TenantId, ct);

        var fromBundle = new SecretBundle(
            JsonSerializer.Deserialize<Dictionary<string, string>>(await secretStore.RetrieveAsync(fromRef, ct))
            ?? new Dictionary<string, string>());
        var toBundle = new SecretBundle(
            JsonSerializer.Deserialize<Dictionary<string, string>>(await secretStore.RetrieveAsync(toRef, ct))
            ?? new Dictionary<string, string>());

        await using ISourceProvider source =
            fromPlugin.CreateSource(ConnectionBuilder.BuildDescriptor(profile.From, fromRef), fromBundle);
        await using IDestinationProvider dest =
            toPlugin.CreateDestination(ConnectionBuilder.BuildDescriptor(profile.To, toRef), toBundle);

        PreflightPlan plan = await analyzer.AnalyzeAsync(source, dest, profile.Scope, ct);

        var output = new PreflightOutput(
            Issues: plan.Issues.Select(i => new PreflightIssueOutput(
                i.IssueType, i.Severity, i.RecommendedAction, i.AffectedPaths, i.Description)).ToList(),
            Estimate: new EstimateOutput(
                plan.Estimate.MailboxCount, plan.Estimate.FolderCount,
                plan.Estimate.MessageCount, plan.Estimate.TotalBytes));
        writer.WritePreflight(output);

        bool blocked = plan.Issues.Any(i => i.Severity == Severity.Blocker);
        return blocked ? CliExitCode.PreflightBlocked : CliExitCode.Success;
    }
}
