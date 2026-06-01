namespace EMaigrator.Infrastructure.Data;

/// <summary>Encrypted credential blob. Purged the instant the owning job reaches a terminal state.</summary>
public class CredentialRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string SecretRef { get; set; } = "";
    public string CipherBlob { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
