using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Infrastructure.Identity;
using Dirija.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dirija.Infrastructure.Seed;

public sealed class InitializerOptions
{
    /// <summary>Drops and recreates the database (demo reset).</summary>
    public bool ResetDatabase { get; init; }
    /// <summary>Fictional data and demo logins with a known password. Must be false on any public deployment.</summary>
    public bool SeedDemoData { get; init; } = true;
    /// <summary>Real admin account, read from environment variables or user-secrets (never from a committed file).</summary>
    public string? AdminEmail { get; init; }
    public string? AdminPassword { get; init; }
    public AgendaOptions Agenda { get; init; } = new();
}

public enum AdminBootstrap
{
    NotConfigured,
    Created,
    AlreadyExists,
    /// <summary>The e-mail belongs to a non-admin account; nothing was changed.</summary>
    EmailInUse,
    /// <summary>The configured password does not meet the password policy; nothing was created.</summary>
    WeakPassword,
    InvalidEmail
}

public static class DbInitializer
{
    /// <summary>Runs at app startup: migrations, demo data, admin bootstrap and agenda generation.</summary>
    public static async Task<AdminBootstrap> InitializeAsync(IServiceProvider services, InitializerOptions options)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        return await InitializeAsync(
            sp.GetRequiredService<DirijaDbContext>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IPasswordHasher<User>>(),
            options);
    }

    public static Task<AdminBootstrap> InitializeAsync(DirijaDbContext db, IClock clock, IPasswordHasher<User> hasher, bool resetDatabase = false) =>
        InitializeAsync(db, clock, hasher, new InitializerOptions { ResetDatabase = resetDatabase });

    public static async Task<AdminBootstrap> InitializeAsync(DirijaDbContext db, IClock clock, IPasswordHasher<User> hasher, InitializerOptions options)
    {
        if (options.ResetDatabase) await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        // WAL lets readers work while a reservation is being written (better concurrency on SQLite).
        if (db.Database.IsSqlite() &&
            !(db.Database.GetConnectionString() ?? "").Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        var now = clock.UtcNow;
        var freshDatabase = !await db.Users.AnyAsync();
        if (options.SeedDemoData)
        {
            if (freshDatabase) await SeedData.SeedAsync(db, now, hasher);
            else await SeedData.TopUpSlotsAsync(db, now);
            await SeedData.EnsureDemoAccountsAsync(db, now, hasher);
        }

        var admin = await EnsureAdminAsync(db, hasher, now, options.AdminEmail, options.AdminPassword);

        var availability = new AvailabilityService(
            new EfInstructorRepository(db), new EfSlotRepository(db), new EfAgendaRepository(db),
            new EfBookingRepository(db), new EfUnitOfWork(db), clock, options.Agenda);
        await availability.SyncAllAsync();

        if (options.SeedDemoData) await SeedData.SeedInstructorDemoLessonAsync(db, now);
        return admin;
    }

    private static async Task<AdminBootstrap> EnsureAdminAsync(DirijaDbContext db, IPasswordHasher<User> hasher, DateTime now,
        string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return AdminBootstrap.NotConfigured;

        var normalized = AccountService.NormalizeEmail(email);
        if (!normalized.Contains('@')) return AdminBootstrap.InvalidEmail;
        if (await db.Users.FirstOrDefaultAsync(u => u.Email == normalized) is { } existing)
            return existing.Role == UserRole.Admin ? AdminBootstrap.AlreadyExists : AdminBootstrap.EmailInUse;
        if (PasswordPolicy.Validate(password) is not null) return AdminBootstrap.WeakPassword;

        var admin = new User { Name = "Administrador", Email = normalized, PasswordHash = "", Role = UserRole.Admin, CreatedAt = now };
        admin.PasswordHash = new IdentityPasswordService(hasher).Hash(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return AdminBootstrap.Created;
    }
}
