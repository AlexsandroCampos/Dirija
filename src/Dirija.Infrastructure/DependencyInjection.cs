using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Infrastructure.Identity;
using Dirija.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dirija.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the EF Core persistence. <paramref name="sqliteConnectionString"/> is resolved lazily so tests can
    /// override configuration. To move to PostgreSQL: swap UseSqlite for UseNpgsql and regenerate migrations.
    /// </summary>
    public static IServiceCollection AddDirijaInfrastructure(
        this IServiceCollection services, Func<IServiceProvider, string> sqliteConnectionString)
    {
        services.AddDbContext<DirijaDbContext>((sp, o) => o.UseSqlite(sqliteConnectionString(sp)));
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IInstructorRepository, EfInstructorRepository>();
        services.AddScoped<ISlotRepository, EfSlotRepository>();
        services.AddScoped<IBookingRepository, EfBookingRepository>();
        services.AddScoped<IReviewRepository, EfReviewRepository>();
        services.AddScoped<IAuthService, PasswordAuthService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        return services;
    }
}
