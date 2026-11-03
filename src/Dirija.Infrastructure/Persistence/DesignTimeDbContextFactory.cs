using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dirija.Infrastructure.Persistence;

/// <summary>Used only by "dotnet ef" to build the model when creating migrations (no database is opened).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DirijaDbContext>
{
    public DirijaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DirijaDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
