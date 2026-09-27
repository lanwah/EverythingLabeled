using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EverythingLabeled.Service.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> does not execute <see cref="ServiceRuntime.RunAsync"/>.
/// </summary>
public sealed class ElDbContextFactory : IDesignTimeDbContextFactory<ElDbContext>
{
    public ElDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ElDbContext>()
            .UseSqlite(DatabaseInitializer.BuildConnectionString("el.design.db"))
            .Options;
        return new ElDbContext(options);
    }
}
