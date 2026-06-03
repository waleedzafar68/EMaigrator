using System;
using Microsoft.AspNetCore.Identity;

namespace EMaigrator.Api.Identity;

/// <summary>
/// The API-local identity principal. Lives in the Api (not Infrastructure) so it can carry the
/// <see cref="TenantId"/> that every authenticated request is scoped to. Stored in the Api's own
/// <see cref="AppIdentityDbContext"/> (separate migrations-history table, same Postgres database).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>The tenant this user belongs to; mirrored into the JWT's <c>tenant_id</c> claim.</summary>
    public Guid TenantId { get; set; }
}
