using System.Diagnostics.CodeAnalysis;

namespace EMaigrator.Core.Configuration;

/// <summary>Per-(provider:account-class) token-bucket config (CONTRACTS.md §7).</summary>
public sealed class RateLimitOptions
{
    // CONTRACTS.md §7 fixes this as a settable dictionary so the IConfiguration binder can
    // assign it; CA2227 (read-only collection) is intentionally suppressed for that contract.
    [SuppressMessage("Usage", "CA2227:Collection properties should be read only",
        Justification = "Options class bound from IConfiguration; the setter is required by the configuration binder (CONTRACTS.md §7).")]
    public Dictionary<string, BucketSpec> Buckets { get; set; } = new();
}
