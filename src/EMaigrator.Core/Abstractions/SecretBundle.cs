namespace EMaigrator.Core.Abstractions;

/// <summary>Decrypted, transient secret values — never logged, scrubbed after use (CONTRACTS.md §2).</summary>
public sealed record SecretBundle(IReadOnlyDictionary<string, string> Values);
