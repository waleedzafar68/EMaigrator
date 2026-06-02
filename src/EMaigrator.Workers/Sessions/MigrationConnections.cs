using System;
using EMaigrator.Core.Abstractions;

namespace EMaigrator.Workers.Sessions;

public sealed record MigrationConnections(
    Guid JobId,
    string TenantId,
    ConnectionDescriptor Source,
    ConnectionDescriptor Dest);
