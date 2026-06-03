using System;

namespace EMaigrator.Workers.Orchestration;

// Internal control messages (NOT frozen Core contracts) — carry only a JobId.
public sealed record PauseJob(Guid JobId);
public sealed record ResumeJob(Guid JobId);
public sealed record CancelJob(Guid JobId);
