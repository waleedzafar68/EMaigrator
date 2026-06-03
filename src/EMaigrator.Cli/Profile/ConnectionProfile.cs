using EMaigrator.Core.Abstractions;   // AuthMethod
using EMaigrator.Core.Model;          // ProviderId

namespace EMaigrator.Cli.Profile;

/// <summary>
/// Non-secret connection description for one side of a migration.
/// There is deliberately NO secret field: secrets only ever come from env or prompt.
/// </summary>
public sealed record ConnectionProfile
{
    public required ProviderId Provider { get; init; }
    public required AuthMethod Auth { get; init; }
    public IReadOnlyDictionary<string, string> Settings { get; init; } =
        new Dictionary<string, string>();
}
