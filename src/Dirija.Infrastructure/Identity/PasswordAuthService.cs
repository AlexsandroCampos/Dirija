using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Microsoft.AspNetCore.Identity;

namespace Dirija.Infrastructure.Identity;

/// <summary>PBKDF2 hashes produced by ASP.NET Core Identity's PasswordHasher.</summary>
public sealed class IdentityPasswordService(IPasswordHasher<User> hasher) : IPasswordService
{
    public string Hash(User user, string password) => hasher.HashPassword(user, password);

    public bool Verify(User user, string password) =>
        hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}

/// <summary>
/// Validates credentials. After <see cref="MaxFailedAttempts"/> consecutive wrong passwords the account is locked
/// for <see cref="LockoutDuration"/>; while locked, even the right password is refused.
/// </summary>
public sealed class PasswordAuthService(IUserRepository users, IPasswordService passwords, IUnitOfWork unitOfWork, IClock clock)
    : IAuthService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<User?> ValidateCredentialsAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return null;

        var user = await users.GetByEmailAsync(email);
        if (user is null) return null;

        var now = clock.UtcNow;
        if (user.LockoutEndUtc is { } until && until > now) return null;

        if (!passwords.Verify(user, password))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEndUtc = now + LockoutDuration;
                user.FailedLoginCount = 0;
            }
            await unitOfWork.SaveChangesAsync();
            return null;
        }

        if (user.FailedLoginCount != 0 || user.LockoutEndUtc is not null)
        {
            user.FailedLoginCount = 0;
            user.LockoutEndUtc = null;
            await unitOfWork.SaveChangesAsync();
        }
        return user;
    }
}
