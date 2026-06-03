using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EMaigrator.Api.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build <see cref="ApiSideContext"/> without the running
/// host (mirrors <c>AppIdentityDbContextFactory</c>). The connection string only fixes the provider at
/// design time; the migration lives under Data/Migrations and is keyed to the
/// <c>__EFMigrationsHistory_ApiSide</c> history table.
/// </summary>
public sealed class ApiSideContextDesignTimeFactory : IDesignTimeDbContextFactory<ApiSideContext>
{
    public ApiSideContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("EMAIGRATOR_DESIGN_CS")
                 ?? "Host=localhost;Database=emaigrator;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<ApiSideContext>()
            .UseNpgsql(cs, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory_ApiSide"))
            .Options;
        return new ApiSideContext(options);
    }
}
