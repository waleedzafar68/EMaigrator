using EMaigrator.Core.Preflight;   // ScopeSpec

namespace EMaigrator.Cli.Profile;

/// <summary>The full self-host migration description. Contains NO secrets.</summary>
public sealed record MigrationProfile
{
    public string TenantId { get; init; } = "self-host";
    public bool StoreSubjects { get; init; }
    public required ConnectionProfile From { get; init; }
    public required ConnectionProfile To { get; init; }
    public required ScopeSpec Scope { get; init; }
}
