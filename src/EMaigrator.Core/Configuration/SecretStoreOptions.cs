namespace EMaigrator.Core.Configuration;

/// <summary>Secret-store mode selection: "LocalKey" | "AzureKeyVault" | "AwsKms" (CONTRACTS.md §7).</summary>
public sealed class SecretStoreOptions
{
    public string Mode { get; set; } = "LocalKey";
    public string? KeyRef { get; set; }
}
