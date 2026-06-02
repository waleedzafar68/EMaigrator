namespace EMaigrator.Connectors.Graph;

/// <summary>
/// Thrown when a Graph <see cref="EMaigrator.Core.Abstractions.ConnectionDescriptor"/> or
/// <see cref="EMaigrator.Core.Abstractions.SecretBundle"/> is missing or malformed.
/// The message must NEVER include a secret value.
/// </summary>
public sealed class GraphConfigurationException : Exception
{
    public GraphConfigurationException(string message) : base(message) { }
}
