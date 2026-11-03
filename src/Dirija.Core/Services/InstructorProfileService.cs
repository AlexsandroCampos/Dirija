using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Geo;

namespace Dirija.Core.Services;

/// <summary>Professional data sent in step 2 of the instructor sign up (and when editing it).</summary>
public record InstructorProfileInput(
    string? DetranCredential,
    LicenseCategory Categories,
    decimal PricePerLesson,
    string? Neighborhood,
    double Latitude,
    double Longitude,
    double ServiceRadiusKm,
    string? Vehicle,
    bool HasDualControl,
    string? MeetingPoint,
    string? Bio);

public enum ProfileError
{
    None,
    Invalid,
    NotFound,
    NotInstructor,
    AlreadyExists,
    InvalidState,
    ReasonRequired
}

public record ProfileResult(ProfileError Error, Instructor? Instructor = null, IReadOnlyDictionary<string, string>? Errors = null,
    bool BackToReview = false)
{
    public bool Success => Error == ProfileError.None;
}

/// <summary>
/// Instructor registration and review. A new or corrected registration is Pending; only an admin moves it to
/// Approved (visible in the search) or Rejected (with a reason the instructor sees).
/// </summary>
public class InstructorProfileService(
    IInstructorRepository instructors,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public const decimal MinPrice = 30m, MaxPrice = 500m;
    public const double MinRadiusKm = 1, MaxRadiusKm = 30;
    public const int MinReasonLength = 10, MaxReasonLength = 300;

    /// <summary>Approximate bounding box of the São Paulo municipality (meeting points must be inside it).</summary>
    public const double MinLat = -24.01, MaxLat = -23.35, MinLng = -46.83, MaxLng = -46.36;

    public Task<Instructor?> GetForUserAsync(int userId) => instructors.GetByUserIdAsync(userId);

    public static IReadOnlyDictionary<string, string> Validate(InstructorProfileInput input)
    {
        var e = new Dictionary<string, string>();
        var credential = (input.DetranCredential ?? "").Trim();
        if (credential.Length is < 4 or > 40 || !credential.All(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '/' or ' '))
            e[nameof(input.DetranCredential)] = "Informe o número da sua credencial de instrutor (4 a 40 caracteres).";
        if ((input.Categories & (LicenseCategory.A | LicenseCategory.B)) == LicenseCategory.None)
            e[nameof(input.Categories)] = "Escolha pelo menos uma categoria.";
        if (input.PricePerLesson is < MinPrice or > MaxPrice)
            e[nameof(input.PricePerLesson)] = $"O preço por aula deve ficar entre R$ {MinPrice:0} e R$ {MaxPrice:0}.";
        if (Neighborhoods.Find(input.Neighborhood) is not { } hood ||
            Neighborhoods.Normalize(input.Neighborhood ?? "") != Neighborhoods.Normalize(hood.Name))
            e[nameof(input.Neighborhood)] = "Escolha um bairro da lista.";
        if (input.Latitude is < MinLat or > MaxLat || input.Longitude is < MinLng or > MaxLng)
            e[nameof(input.Latitude)] = "O ponto de encontro precisa ficar no município de São Paulo.";
        if (input.ServiceRadiusKm is < MinRadiusKm or > MaxRadiusKm)
            e[nameof(input.ServiceRadiusKm)] = $"O raio deve ficar entre {MinRadiusKm:0} e {MaxRadiusKm:0} km.";
        if ((input.Vehicle ?? "").Trim().Length is < 3 or > 200)
            e[nameof(input.Vehicle)] = "Descreva o veículo (ex.: Onix 2022, duplo comando).";
        if ((input.MeetingPoint ?? "").Trim().Length is < 5 or > 200)
            e[nameof(input.MeetingPoint)] = "Descreva o ponto de encontro (ex.: Estação Faria Lima, saída Rua dos Pinheiros).";
        if ((input.Bio ?? "").Trim().Length > 1000)
            e[nameof(input.Bio)] = "A descrição pode ter no máximo 1000 caracteres.";
        return e;
    }

    public async Task<ProfileResult> SubmitAsync(int userId, InstructorProfileInput input)
    {
        var user = await users.GetByIdAsync(userId);
        if (user is null) return new ProfileResult(ProfileError.NotFound);
        if (user.Role != UserRole.Instructor) return new ProfileResult(ProfileError.NotInstructor);
        if (await instructors.GetByUserIdAsync(userId) is not null) return new ProfileResult(ProfileError.AlreadyExists);

        var errors = Validate(input);
        if (errors.Count > 0) return new ProfileResult(ProfileError.Invalid, Errors: errors);

        var instructor = new Instructor
        {
            UserId = userId, Name = user.Name, Neighborhood = "", Status = InstructorStatus.Pending, SubmittedAt = clock.UtcNow
        };
        Apply(instructor, input);
        instructors.Add(instructor);
        try
        {
            await unitOfWork.SaveChangesAsync();
        }
        catch (UniqueConstraintException)
        {
            unitOfWork.DiscardChanges();
            return new ProfileResult(ProfileError.AlreadyExists);
        }
        return new ProfileResult(ProfileError.None, instructor);
    }

    /// <summary>
    /// Saves edits. A rejected registration goes back to review; an approved one goes back to review only when the
    /// credential number or the categories change (price, vehicle, meeting point and description do not need review).
    /// </summary>
    public async Task<ProfileResult> UpdateAsync(int userId, InstructorProfileInput input)
    {
        var instructor = await instructors.GetByUserIdAsync(userId);
        if (instructor is null) return new ProfileResult(ProfileError.NotFound);

        var errors = Validate(input);
        if (errors.Count > 0) return new ProfileResult(ProfileError.Invalid, instructor, errors);

        var credentialChanged = !string.Equals(instructor.DetranCredential, (input.DetranCredential ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        var categoriesChanged = instructor.Categories != input.Categories;
        var backToReview = instructor.Status == InstructorStatus.Rejected ||
                           (instructor.Status == InstructorStatus.Approved && (credentialChanged || categoriesChanged));

        Apply(instructor, input);
        if (backToReview)
        {
            instructor.Status = InstructorStatus.Pending;
            instructor.RejectionReason = null;
            instructor.SubmittedAt = clock.UtcNow;
        }
        await unitOfWork.SaveChangesAsync();
        return new ProfileResult(ProfileError.None, instructor, BackToReview: backToReview);
    }

    public async Task<ProfileResult> ApproveAsync(int adminUserId, int instructorId)
    {
        var instructor = await instructors.GetByIdAsync(instructorId);
        if (instructor is null || instructor.UserId is null) return new ProfileResult(ProfileError.NotFound);
        if (instructor.Status != InstructorStatus.Pending) return new ProfileResult(ProfileError.InvalidState, instructor);

        instructor.Status = InstructorStatus.Approved;
        instructor.RejectionReason = null;
        instructor.ReviewedAt = clock.UtcNow;
        instructor.ReviewedByUserId = adminUserId;
        await unitOfWork.SaveChangesAsync();
        return new ProfileResult(ProfileError.None, instructor);
    }

    public async Task<ProfileResult> RejectAsync(int adminUserId, int instructorId, string? reason)
    {
        var instructor = await instructors.GetByIdAsync(instructorId);
        if (instructor is null || instructor.UserId is null) return new ProfileResult(ProfileError.NotFound);
        if (instructor.Status != InstructorStatus.Pending) return new ProfileResult(ProfileError.InvalidState, instructor);

        var text = (reason ?? "").Trim();
        if (text.Length is < MinReasonLength or > MaxReasonLength) return new ProfileResult(ProfileError.ReasonRequired, instructor);

        instructor.Status = InstructorStatus.Rejected;
        instructor.RejectionReason = text;
        instructor.ReviewedAt = clock.UtcNow;
        instructor.ReviewedByUserId = adminUserId;
        await unitOfWork.SaveChangesAsync();
        return new ProfileResult(ProfileError.None, instructor);
    }

    private static void Apply(Instructor i, InstructorProfileInput input)
    {
        i.DetranCredential = input.DetranCredential!.Trim();
        i.Categories = input.Categories & (LicenseCategory.A | LicenseCategory.B);
        i.PricePerLesson = Math.Round(input.PricePerLesson, 2);
        i.Neighborhood = Neighborhoods.Find(input.Neighborhood)!.Name;
        i.Latitude = Math.Round(input.Latitude, 6);
        i.Longitude = Math.Round(input.Longitude, 6);
        i.ServiceRadiusKm = Math.Round(input.ServiceRadiusKm, 1);
        i.Vehicle = input.Vehicle!.Trim();
        i.HasDualControl = input.HasDualControl;
        i.MeetingPoint = input.MeetingPoint!.Trim();
        i.Bio = (input.Bio ?? "").Trim();
    }
}
