namespace EMaigrator.Infrastructure.Data;

/// <summary>Whether a job is a normal migration or a reconcile/repair run against the live destination.</summary>
public enum JobMode
{
    Migrate = 0,
    Reconcile = 1,
}
