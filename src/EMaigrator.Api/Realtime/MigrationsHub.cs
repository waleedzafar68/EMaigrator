using System;
using System.Threading.Tasks;
using EMaigrator.Api.Identity;
using EMaigrator.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Realtime;

/// <summary>
/// Client → server hub (CONTRACTS §6). A connection joins a per-migration SignalR group only after the
/// hub verifies the migration belongs to the caller's tenant.
/// <para>
/// IMPORTANT: tenant authorization here derives the tenant from the connection's authenticated principal
/// (the <see cref="JwtTokenIssuer.TenantClaim"/> claim) and queries with an EXPLICIT tenant predicate.
/// It does NOT rely on <see cref="EmaigratorDbContext"/>'s ambient query filter: that filter reads
/// <c>ICurrentTenant</c> → <c>IHttpContextAccessor.HttpContext</c>, which is typically null during a hub
/// method invocation (WebSocket transport). With a null HttpContext the filter falls back to
/// <see cref="Guid.Empty"/> and runs UNFILTERED, so relying on it would let a cross-tenant Subscribe
/// wrongly succeed. The explicit predicate scopes the ownership check correctly regardless.
/// </para>
/// </summary>
[Authorize]
public sealed class MigrationsHub : Hub<IMigrationProgressClient>
{
    private readonly EmaigratorDbContext _db;

    public MigrationsHub(EmaigratorDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task Subscribe(string migrationId)
    {
        if (!Guid.TryParse(migrationId, out var id))
        {
            throw new HubException("Invalid migration id.");
        }

        var tenantClaim = Context.User?.FindFirst(JwtTokenIssuer.TenantClaim)?.Value;
        if (!Guid.TryParse(tenantClaim, out var tenant))
        {
            throw new HubException("No tenant context.");
        }

        // Explicit tenant predicate (see class remarks): never depend on the ambient filter in a hub.
        var owned = await _db.Set<Job>().AnyAsync(j => j.Id == id && j.TenantId == tenant).ConfigureAwait(false);
        if (!owned)
        {
            throw new HubException("Not authorized for this migration.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, migrationId).ConfigureAwait(false);
    }

    public Task Unsubscribe(string migrationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, migrationId);
}
