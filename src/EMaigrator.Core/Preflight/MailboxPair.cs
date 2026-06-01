namespace EMaigrator.Core.Preflight;

/// <summary>One source→dest mailbox pair (the billing unit) (CONTRACTS.md §3).</summary>
public sealed record MailboxPair(string SourceMailbox, string DestMailbox);
