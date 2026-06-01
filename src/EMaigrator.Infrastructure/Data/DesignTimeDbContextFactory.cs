using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EMaigrator.Infrastructure.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EmaigratorDbContext>
{
    public EmaigratorDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("EMAIGRATOR_DESIGN_CS")
                 ?? "Host=localhost;Database=emaigrator;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<EmaigratorDbContext>()
            .UseNpgsql(cs, npg => npg.MigrationsAssembly("EMaigrator.Infrastructure"))
            .Options;
        return new EmaigratorDbContext(options);
    }
}
