using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Microsoft.AspNetCore.Identity;

namespace Dirija.Infrastructure.Identity;

/// <summary>Validates credentials against the PBKDF2 hash produced by ASP.NET Core Identity's PasswordHasher.</summary>
public sealed class PasswordAuthService(IUserRepository users, IPasswordHasher<User> hasher) : IAuthService
{
    public async Task<User?> ValidateCredentialsAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return null;

        var user = await users.GetByEmailAsync(email);
        if (user is null) return null;

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : user;
    }
}
