using EMaigrator.Core.Model;

namespace EMaigrator.Infrastructure.Data;

public class Job
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public ProviderId SourceProvider { get; set; }
    public ProviderId DestProvider { get; set; }
    public string? SourceConnectionRef { get; set; }
    public string? DestConnectionRef { get; set; }
    public bool IsBatch { get; set; }
    public JobStatus Status { get; set; }
    public JobMode Mode { get; set; }
    public int WizardStep { get; set; }
    public bool StoreSubjects { get; set; }

    /// <summary>Optional scope window (inclusive lower bound) applied to source reads — migrate AND reconcile.</summary>
    public DateTimeOffset? Since { get; set; }

    /// <summary>Optional scope window (exclusive upper bound) applied to source reads — migrate AND reconcile.</summary>
    public DateTimeOffset? Before { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
