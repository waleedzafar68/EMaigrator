using EMaigrator.Core.Model;

namespace EMaigrator.Connectors.Gmail;

/// <summary>
/// Stable type for assembly-level reflection in tests and DI scanning. Exposing the provider id
/// also anchors the <c>EMaigrator.Core</c> reference into this assembly's metadata, so the
/// dependency-rule guard can observe the Core reference before the providers are implemented.
/// </summary>
public sealed class AssemblyMarker
{
    /// <summary>The canonical provider id this assembly implements.</summary>
    public static ProviderId Provider { get; } = new("gmail");
}
