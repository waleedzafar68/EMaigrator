using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EMaigrator.Api.Identity;

/// <summary>
/// The Api-local Identity store. Points at the same Postgres database as the engine's
/// <c>EmaigratorDbContext</c> but uses a distinct EF migrations-history table
/// (<c>__EFMigrationsHistory_Identity</c>) so the two contexts' migrations never collide. The engine
/// context cannot host Identity because Infrastructure may not reference <see cref="ApplicationUser"/>
/// (the dependency rule), so the user store lives here in the Api.
/// </summary>
public sealed class AppIdentityDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) : base(options)
    {
    }
}
