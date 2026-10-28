using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dirija.Infrastructure.Seed;

public static class DbInitializer
{
    /// <summary>Runs at app startup: migrations, first-run seed and agenda top-up.</summary>
    public static async Task InitializeAsync(IServiceProvider services, bool resetDatabase = false)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        await InitializeAsync(
            sp.GetRequiredService<DirijaDbContext>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IPasswordHasher<User>>(),
            resetDatabase);
    }

    public static async Task InitializeAsync(DirijaDbContext db, IClock clock, IPasswordHasher<User> hasher, bool resetDatabase = false)
    {
        if (resetDatabase) await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        // WAL lets readers work while a reservation is being written (better concurrency on SQLite).
        if (db.Database.IsSqlite() &&
            !(db.Database.GetConnectionString() ?? "").Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        if (!await db.Users.AnyAsync())
            await SeedData.SeedAsync(db, clock.UtcNow, hasher);
        else
            await SeedData.TopUpSlotsAsync(db, clock.UtcNow);
    }
}
