namespace EMaigrator.Infrastructure.Messaging;

/// <summary>
/// Infra-local control signal: cancel a job. Not in CONTRACTS.md §4 (which covers
/// data-plane messages); consumed by Workers (Plan 07). Promoting this to the shared contract
/// is a coordination event.
/// </summary>
public sealed record CancelJob(Guid JobId);
