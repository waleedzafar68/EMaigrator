using System.Diagnostics.CodeAnalysis;

namespace EMaigrator.Core.Model;

/// <summary>Canonical message flags (CONTRACTS.md §1).</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Name is fixed by CONTRACTS.md §1; '[Flags] enum MessageFlags' is the idiomatic .NET form for a flags enum and is referenced across the whole plan set.")]
public enum MessageFlags
{
    None = 0,
    Seen = 1,
    Answered = 2,
    Flagged = 4,
    Draft = 8,
    Deleted = 16,
}
