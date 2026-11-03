using System.ComponentModel.DataAnnotations;
using Dirija.Core.Entities;
using Dirija.Core.Services;

namespace Dirija.Web.Models;

public class RegisterViewModel
{
    /// <summary>"Student" or "Instructor". Admin accounts are never created here.</summary>
    [Required(ErrorMessage = "Escolha se você é aluno ou instrutor.")]
    public string Role { get; set; } = Roles.Student;

    [Required(ErrorMessage = "Informe seu nome completo.")]
    [StringLength(120, ErrorMessage = "Máximo de 120 caracteres.")]
    [Display(Name = "Nome completo")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(200)]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Informe seu telefone.")]
    [Display(Name = "Telefone com DDD")]
    public string Phone { get; set; } = "";

    [Display(Name = "Seu bairro")]
    public string? Neighborhood { get; set; }

    [Required(ErrorMessage = "Crie uma senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Repita a senha.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "As senhas não são iguais.")]
    [Display(Name = "Repita a senha")]
    public string ConfirmPassword { get; set; } = "";

    [Range(typeof(bool), "true", "true", ErrorMessage = "É preciso concordar para criar a conta.")]
    public bool AcceptTerms { get; set; }

    public UserRole ParsedRole => Role == Roles.Instructor ? UserRole.Instructor : UserRole.Student;
}

public class AccountDataViewModel
{
    [Required(ErrorMessage = "Informe seu nome completo.")]
    [StringLength(120)]
    [Display(Name = "Nome completo")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Informe seu telefone.")]
    [Display(Name = "Telefone com DDD")]
    public string Phone { get; set; } = "";

    [Display(Name = "Seu bairro")]
    public string? Neighborhood { get; set; }

    public string Email { get; set; } = "";
    public bool IsStudent { get; set; }
}

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Informe sua senha atual.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha atual")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Informe a nova senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nova senha")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Repita a nova senha.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "As senhas não são iguais.")]
    [Display(Name = "Repita a nova senha")]
    public string ConfirmNewPassword { get; set; } = "";
}

/// <summary>Step 2 of the instructor sign up (also used to correct and resubmit).</summary>
public class InstructorProfileFormModel
{
    [Display(Name = "Número da credencial de instrutor")]
    public string? DetranCredential { get; set; }

    public bool CategoryA { get; set; }
    public bool CategoryB { get; set; } = true;

    [Display(Name = "Preço por aula (50 min)")]
    public decimal? PricePerLesson { get; set; }

    [Display(Name = "Bairro do ponto de encontro")]
    public string? Neighborhood { get; set; }

    /// <summary>Strings parsed with the invariant culture: the map writes "-23.561400" and the request culture is pt-BR.</summary>
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }

    [Display(Name = "Atendo alunos a até (km)")]
    public double ServiceRadiusKm { get; set; } = 8;

    [Display(Name = "Veículo")]
    public string? Vehicle { get; set; }

    [Display(Name = "Carro com duplo comando")]
    public bool HasDualControl { get; set; }

    [Display(Name = "Ponto de encontro")]
    public string? MeetingPoint { get; set; }

    [Display(Name = "Sobre você e suas aulas (opcional)")]
    public string? Bio { get; set; }

    public InstructorProfileInput ToInput() => new(
        DetranCredential,
        (CategoryA ? LicenseCategory.A : LicenseCategory.None) | (CategoryB ? LicenseCategory.B : LicenseCategory.None),
        PricePerLesson ?? 0, Neighborhood, ParseCoordinate(Latitude), ParseCoordinate(Longitude), ServiceRadiusKm,
        Vehicle, HasDualControl, MeetingPoint, Bio);

    private static double ParseCoordinate(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    public static InstructorProfileFormModel From(Instructor i) => new()
    {
        DetranCredential = i.DetranCredential,
        CategoryA = i.Categories.HasFlag(LicenseCategory.A),
        CategoryB = i.Categories.HasFlag(LicenseCategory.B),
        PricePerLesson = i.PricePerLesson,
        Neighborhood = i.Neighborhood,
        Latitude = i.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Longitude = i.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ServiceRadiusKm = i.ServiceRadiusKm,
        Vehicle = i.Vehicle,
        HasDualControl = i.HasDualControl,
        MeetingPoint = i.MeetingPoint,
        Bio = i.Bio
    };
}

public class InstructorOnboardingViewModel
{
    public required InstructorProfileFormModel Form { get; init; }
    public Instructor? Existing { get; init; }
    public bool IsNew => Existing is null;
}
