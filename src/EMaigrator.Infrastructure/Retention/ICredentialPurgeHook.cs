namespace EMaigrator.Infrastructure.Retention;

/// <summary>Purges all stored credentials for a job once it reaches a terminal state (DESIGN.md §10).</summary>
public interface ICredentialPurgeHook
{
    Task PurgeForJobAsync(Guid jobId, CancellationToken ct);
}
