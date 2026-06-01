namespace EMaigrator.Infrastructure.Data;

public enum JobStatus
{
    Draft,
    Queued,
    PreFlight,
    AwaitingApproval,
    Running,
    Paused,
    Completed,
    Partial,
    Failed,
    Cancelled,
}
