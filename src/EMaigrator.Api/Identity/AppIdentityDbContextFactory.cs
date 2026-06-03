using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EMaigrator.Api.Identity;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build <see cref="AppIdentityDbContext"/> without the
/// running host (mirrors the engine's DesignTimeDbContextFactory). The connection string is only used
/// to know the provider at design time; the migration lives in this project under Identity/Migrations
/// and is keyed to the <c>__EFMigrationsHistory_Identity</c> history table.
/// </summary>
public sealed class AppIdentityDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("EMAIGRATOR_DESIGN_CS")
                 ?? "Host=localhost;Database=emaigrator;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseNpgsql(cs, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory_Identity"))
            .Options;
        return new AppIdentityDbContext(options);
    }
}
