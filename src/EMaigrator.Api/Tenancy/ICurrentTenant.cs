using System;

namespace EMaigrator.Api.Tenancy;

/// <summary>
/// Per-request accessor for the authenticated caller's tenant. Populated from the
/// <c>tenant_id</c> JWT claim. The API uses this to scope the engine's <c>EmaigratorDbContext</c>
/// (via its <c>CurrentTenantId</c> sentinel) so tenant-scoped reads never cross tenants.
/// </summary>
public interface ICurrentTenant
{
    /// <summary>
    /// The caller's tenant id. Throws <see cref="UnauthorizedAccessException"/> when the request
    /// carries no parseable tenant context.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>True when a parseable tenant context is present on the request.</summary>
    bool IsAuthenticated { get; }
}
