using System.Net.Mail;
using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Geo;

namespace Dirija.Core.Services;

public enum AccountError
{
    None,
    Invalid,
    EmailTaken,
    WeakPassword,
    WrongPassword,
    NotFound
}

public record AccountResult(AccountError Error, User? User = null, IReadOnlyDictionary<string, string>? Errors = null)
{
    public bool Success => Error == AccountError.None;
    public static AccountResult Ok(User user) => new(AccountError.None, user);
    public static AccountResult Fail(AccountError error, IReadOnlyDictionary<string, string>? errors = null) => new(error, null, errors);
}

/// <summary>Self-service sign up is only for students and instructors; admins are created from configuration.</summary>
public record RegisterInput(UserRole Role, string? Name, string? Email, string? Phone, string? Neighborhood, string? Password);

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    /// <summary>Returns an error message in Portuguese, or null when the password is acceptable.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"A senha precisa ter pelo menos {MinLength} caracteres.";
        if (password.Length > MaxLength) return $"A senha pode ter no máximo {MaxLength} caracteres.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "A senha precisa ter letras e números.";
        return null;
    }
}

public class AccountService(
    IUserRepository users,
    IInstructorRepository instructors,
    IPasswordService passwords,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    public static string? NormalizePhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsAsciiDigit).ToArray());
        return digits.Length is 10 or 11 ? digits : null;
    }

    public async Task<AccountResult> RegisterAsync(RegisterInput input)
    {
        var errors = new Dictionary<string, string>();
        if (input.Role is not (UserRole.Student or UserRole.Instructor)) errors["Role"] = "Escolha se você é aluno ou instrutor.";

        var name = (input.Name ?? "").Trim();
        if (name.Length is < 3 or > 120 || !name.Contains(' ')) errors["Name"] = "Informe seu nome completo.";

        var email = NormalizeEmail(input.Email);
        if (!IsValidEmail(email)) errors["Email"] = "E-mail inválido.";

        var phone = NormalizePhone(input.Phone);
        if (phone is null) errors["Phone"] = "Informe um telefone com DDD (10 ou 11 dígitos).";

        var hood = Neighborhoods.Find(input.Neighborhood);
        if (input.Role == UserRole.Student && (hood is null || !IsExact(input.Neighborhood, hood)))
            errors["Neighborhood"] = "Escolha um bairro da lista.";

        if (PasswordPolicy.Validate(input.Password) is { } pwError) errors["Password"] = pwError;
        if (errors.Count > 0)
            return AccountResult.Fail(errors.ContainsKey("Password") && errors.Count == 1 ? AccountError.WeakPassword : AccountError.Invalid, errors);

        if (await users.GetByEmailAsync(email) is not null) return EmailTaken();

        var user = new User
        {
            Name = name, Email = email, Phone = FormatPhone(phone!), Role = input.Role, PasswordHash = "",
            Neighborhood = input.Role == UserRole.Student ? hood!.Name : "", CreatedAt = clock.UtcNow
        };
        user.PasswordHash = passwords.Hash(user, input.Password!);
        users.Add(user);
        try
        {
            await unitOfWork.SaveChangesAsync();
        }
        catch (UniqueConstraintException)
        {
            // Two sign ups with the same e-mail at the same time: the unique index keeps only the first one.
            unitOfWork.DiscardChanges();
            return EmailTaken();
        }
        return AccountResult.Ok(user);
    }

    /// <summary>Edits the account data. Instructors keep their public name in sync.</summary>
    public async Task<AccountResult> UpdateDataAsync(int userId, string? name, string? phone, string? neighborhood)
    {
        var user = await users.GetByIdAsync(userId);
        if (user is null) return AccountResult.Fail(AccountError.NotFound);

        var errors = new Dictionary<string, string>();
        var cleanName = (name ?? "").Trim();
        if (cleanName.Length is < 3 or > 120 || !cleanName.Contains(' ')) errors["Name"] = "Informe seu nome completo.";
        var cleanPhone = NormalizePhone(phone);
        if (cleanPhone is null) errors["Phone"] = "Informe um telefone com DDD (10 ou 11 dígitos).";
        Neighborhood? hood = null;
        if (user.Role == UserRole.Student)
        {
            hood = Neighborhoods.Find(neighborhood);
            if (hood is null || !IsExact(neighborhood, hood)) errors["Neighborhood"] = "Escolha um bairro da lista.";
        }
        if (errors.Count > 0) return AccountResult.Fail(AccountError.Invalid, errors);

        user.Name = cleanName;
        user.Phone = FormatPhone(cleanPhone!);
        if (hood is not null) user.Neighborhood = hood.Name;
        if (user.Role == UserRole.Instructor && await instructors.GetByUserIdAsync(user.Id) is { } instructor)
            instructor.Name = cleanName;

        await unitOfWork.SaveChangesAsync();
        return AccountResult.Ok(user);
    }

    public async Task<AccountResult> ChangePasswordAsync(int userId, string? currentPassword, string? newPassword)
    {
        var user = await users.GetByIdAsync(userId);
        if (user is null) return AccountResult.Fail(AccountError.NotFound);
        if (string.IsNullOrEmpty(currentPassword) || !passwords.Verify(user, currentPassword))
            return AccountResult.Fail(AccountError.WrongPassword, new Dictionary<string, string> { ["CurrentPassword"] = "Senha atual incorreta." });
        if (PasswordPolicy.Validate(newPassword) is { } error)
            return AccountResult.Fail(AccountError.WeakPassword, new Dictionary<string, string> { ["NewPassword"] = error });

        user.PasswordHash = passwords.Hash(user, newPassword!);
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        await unitOfWork.SaveChangesAsync();
        return AccountResult.Ok(user);
    }

    /// <summary>Formats 11987654321 as "(11) 98765-4321" and 1133334444 as "(11) 3333-4444".</summary>
    public static string FormatPhone(string digits) => digits.Length == 11
        ? $"({digits[..2]}) {digits[2..7]}-{digits[7..]}"
        : $"({digits[..2]}) {digits[2..6]}-{digits[6..]}";

    private static bool IsExact(string? input, Neighborhood hood) =>
        Neighborhoods.Normalize(input ?? "") == Neighborhoods.Normalize(hood.Name);

    private static bool IsValidEmail(string email)
    {
        if (email.Length is < 6 or > 200 || email.Contains(' ')) return false;
        try
        {
            var address = new MailAddress(email);
            return address.Address == email && address.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static AccountResult EmailTaken() =>
        AccountResult.Fail(AccountError.EmailTaken, new Dictionary<string, string> { ["Email"] = "Já existe uma conta com este e-mail." });
}
