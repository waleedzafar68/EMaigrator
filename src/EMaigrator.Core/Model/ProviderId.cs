namespace EMaigrator.Core.Model;

/// <summary>Provider identity: "imap", "graph", "gmail". (CONTRACTS.md §1)</summary>
public readonly record struct ProviderId(string Value)
{
    public override string ToString() => Value;
}
