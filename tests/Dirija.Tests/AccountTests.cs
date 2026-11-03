using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Infrastructure.Identity;
using Dirija.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace Dirija.Tests;

public class AccountTests : IAsyncLifetime
{
    private DomainFixture _f = null!;
    public async Task InitializeAsync() => _f = await DomainFixture.CreateAsync();
    public async Task DisposeAsync() => await _f.DisposeAsync();

    private static RegisterInput Student(string email = "nova@dirija.com", string password = "senhaboa1") =>
        new(UserRole.Student, "Nova Aluna", email, "(11) 98765-4321", "Moema", password);

    [Fact]
    public async Task Register_Student_CreatesUserWithHashAndNormalizedData()
    {
        var result = await _f.Accounts.RegisterAsync(Student("  NOVA@Dirija.com "));

        Assert.True(result.Success);
        await using var db = _f.NewContext();
        var saved = await db.Users.SingleAsync(u => u.Id == result.User!.Id);
        Assert.Equal("nova@dirija.com", saved.Email);
        Assert.Equal(UserRole.Student, saved.Role);
        Assert.Equal("(11) 98765-4321", saved.Phone);
        Assert.Equal("Moema", saved.Neighborhood);
        Assert.DoesNotContain("senhaboa1", saved.PasswordHash);
        Assert.NotNull(await _f.Auth.ValidateCredentialsAsync("nova@dirija.com", "senhaboa1"));
    }

    [Fact]
    public async Task Register_ExistingEmailWithOtherCase_IsRejected()
    {
        var result = await _f.Accounts.RegisterAsync(Student("ALUNO@dirija.com"));

        Assert.Equal(AccountError.EmailTaken, result.Error);
        Assert.Contains("Email", result.Errors!.Keys);
    }

    [Theory]
    [InlineData("curta1")]       // too short
    [InlineData("somenteletras")] // no digit
    [InlineData("12345678")]     // no letter
    public async Task Register_WeakPassword_IsRejected(string password)
    {
        var result = await _f.Accounts.RegisterAsync(Student(password: password));

        Assert.Equal(AccountError.WeakPassword, result.Error);
        Assert.False(await _f.Db.Users.AnyAsync(u => u.Email == "nova@dirija.com"));
    }

    [Fact]
    public async Task Register_AsAdmin_IsNeverAllowed()
    {
        var result = await _f.Accounts.RegisterAsync(Student() with { Role = UserRole.Admin });

        Assert.Equal(AccountError.Invalid, result.Error);
        Assert.Contains("Role", result.Errors!.Keys);
    }

    [Fact]
    public async Task Register_InvalidPhoneAndNeighborhood_ReturnFieldErrors()
    {
        var result = await _f.Accounts.RegisterAsync(Student() with { Phone = "123", Neighborhood = "Copacabana" });

        Assert.Equal(AccountError.Invalid, result.Error);
        Assert.Contains("Phone", result.Errors!.Keys);
        Assert.Contains("Neighborhood", result.Errors!.Keys);
    }

    [Fact]
    public async Task DuplicateEmail_PastThePreCheck_IsStoppedByTheUniqueIndex()
    {
        // Simulates two sign ups that both passed the "e-mail exists?" check: the database keeps only one.
        await using var db = _f.NewContext();
        new Infrastructure.Persistence.EfUserRepository(db).Add(new User
        {
            Name = "Outra Pessoa", Email = SeedData.DemoStudentEmail, PasswordHash = "x", CreatedAt = _f.Clock.UtcNow
        });

        await Assert.ThrowsAsync<Core.Abstractions.UniqueConstraintException>(
            () => new Infrastructure.Persistence.EfUnitOfWork(db).SaveChangesAsync());
        await using var check = _f.NewContext();
        Assert.Equal(1, await check.Users.CountAsync(u => u.Email == SeedData.DemoStudentEmail));
    }

    // ------------------------------------------------------------------ login lockout

    [Fact]
    public async Task Login_FiveWrongPasswords_LocksEvenTheRightPassword_For15Minutes()
    {
        for (var i = 0; i < PasswordAuthService.MaxFailedAttempts; i++)
            Assert.Null(await _f.Auth.ValidateCredentialsAsync(SeedData.DemoStudentEmail, "errada"));

        Assert.Null(await _f.Auth.ValidateCredentialsAsync(SeedData.DemoStudentEmail, SeedData.DemoPassword));

        _f.Clock.Advance(PasswordAuthService.LockoutDuration + TimeSpan.FromSeconds(1));
        Assert.NotNull(await _f.Auth.ValidateCredentialsAsync(SeedData.DemoStudentEmail, SeedData.DemoPassword));
    }

    [Fact]
    public async Task Login_Success_ResetsTheFailureCounter()
    {
        for (var i = 0; i < PasswordAuthService.MaxFailedAttempts - 1; i++)
            await _f.Auth.ValidateCredentialsAsync(SeedData.DemoStudentEmail, "errada");
        Assert.NotNull(await _f.Auth.ValidateCredentialsAsync(SeedData.DemoStudentEmail, SeedData.DemoPassword));

        // Four more mistakes do not lock, because the counter started again from zero.
        for (var i = 0; i < PasswordAuthService.MaxFailedAttempts - 1; i++)
            await _f.Auth.ValidateCredentialsAsync(SeedData.DemoStudentEmail, "errada");
        Assert.NotNull(await _f.Auth.ValidateCredentialsAsync(SeedData.DemoStudentEmail, SeedData.DemoPassword));
    }

    // ------------------------------------------------------------------ edit data / password

    [Fact]
    public async Task ChangePassword_RequiresCurrentPassword()
    {
        var wrong = await _f.Accounts.ChangePasswordAsync(1, "errada", "novasenha9");
        Assert.Equal(AccountError.WrongPassword, wrong.Error);

        var weak = await _f.Accounts.ChangePasswordAsync(1, SeedData.DemoPassword, "curta");
        Assert.Equal(AccountError.WeakPassword, weak.Error);

        Assert.True((await _f.Accounts.ChangePasswordAsync(1, SeedData.DemoPassword, "novasenha9")).Success);
        Assert.Null(await _f.Auth.ValidateCredentialsAsync(SeedData.DemoStudentEmail, SeedData.DemoPassword));
        Assert.NotNull(await _f.Auth.ValidateCredentialsAsync(SeedData.DemoStudentEmail, "novasenha9"));
    }

    [Fact]
    public async Task UpdateData_Instructor_KeepsPublicNameInSync()
    {
        var user = await _f.Db.Users.SingleAsync(u => u.Email == SeedData.DemoInstructorEmail);

        var result = await _f.Accounts.UpdateDataAsync(user.Id, "Carlos Mendes Filho", "11 91111-2222", null);

        Assert.True(result.Success);
        await using var db = _f.NewContext();
        Assert.Equal("Carlos Mendes Filho", (await db.Instructors.SingleAsync(i => i.UserId == user.Id)).Name);
        Assert.Equal("(11) 91111-2222", (await db.Users.SingleAsync(u => u.Id == user.Id)).Phone);
    }

    [Fact]
    public async Task Seed_CreatesDemoInstructorPendingAndAdminAccounts()
    {
        var instructor = await _f.Db.Users.SingleAsync(u => u.Email == SeedData.DemoInstructorEmail);
        var pending = await _f.Db.Users.SingleAsync(u => u.Email == SeedData.DemoPendingInstructorEmail);
        var admin = await _f.Db.Users.SingleAsync(u => u.Email == SeedData.DemoAdminEmail);

        Assert.Equal(UserRole.Instructor, instructor.Role);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.Equal(InstructorStatus.Approved, (await _f.Db.Instructors.SingleAsync(i => i.UserId == instructor.Id)).Status);
        var tiago = await _f.Db.Instructors.SingleAsync(i => i.UserId == pending.Id);
        Assert.Equal(InstructorStatus.Pending, tiago.Status);
        Assert.False(tiago.IsDemo);
        Assert.True(await _f.Db.WeeklyAvailability.AnyAsync(r => r.InstructorId == tiago.Id));
    }
}

public class InstructorProfileTests : IAsyncLifetime
{
    private DomainFixture _f = null!;
    public async Task InitializeAsync() => _f = await DomainFixture.CreateAsync();
    public async Task DisposeAsync() => await _f.DisposeAsync();

    private static InstructorProfileInput ValidInput(string credential = "SP-777001", LicenseCategory cats = LicenseCategory.B) => new(
        credential, cats, 95m, "Lapa", -23.5270, -46.7040, 8, "Onix 2022", true, "Estação Lapa (CPTM)", "Aulas com calma.");

    private async Task<int> NewInstructorUserAsync(string email = "novo.instrutor@dirija.com")
    {
        var result = await _f.Accounts.RegisterAsync(new RegisterInput(UserRole.Instructor, "Novo Instrutor", email, "11987654321", null, "senhaboa1"));
        Assert.True(result.Success);
        return result.User!.Id;
    }

    private async Task<int> AdminIdAsync() => (await _f.Db.Users.SingleAsync(u => u.Email == SeedData.DemoAdminEmail)).Id;

    [Fact]
    public async Task Submit_CreatesPendingProfile_ThatIsNotInTheSearch()
    {
        var userId = await NewInstructorUserAsync();

        var result = await _f.Profiles.SubmitAsync(userId, ValidInput());

        Assert.True(result.Success);
        Assert.Equal(InstructorStatus.Pending, result.Instructor!.Status);
        Assert.Equal("Novo Instrutor", result.Instructor.Name);
        var search = await _f.Search.SearchAsync(new SearchQuery { Neighborhood = "Lapa", RadiusKm = 5 });
        Assert.DoesNotContain(search.Items, i => i.Instructor.Id == result.Instructor.Id);
    }

    [Fact]
    public async Task Approve_MakesTheInstructorAppearInTheSearch()
    {
        var instructor = (await _f.Profiles.SubmitAsync(await NewInstructorUserAsync(), ValidInput())).Instructor!;

        var result = await _f.Profiles.ApproveAsync(await AdminIdAsync(), instructor.Id);

        Assert.True(result.Success);
        var search = await _f.Search.SearchAsync(new SearchQuery { Neighborhood = "Lapa", RadiusKm = 5 });
        Assert.Contains(search.Items, i => i.Instructor.Id == instructor.Id);
        // Approving twice is refused: the registration is no longer pending.
        Assert.Equal(ProfileError.InvalidState, (await _f.Profiles.ApproveAsync(await AdminIdAsync(), instructor.Id)).Error);
    }

    [Fact]
    public async Task Reject_RequiresAReason_AndTheInstructorCanResubmit()
    {
        var userId = await NewInstructorUserAsync();
        var instructor = (await _f.Profiles.SubmitAsync(userId, ValidInput())).Instructor!;
        var admin = await AdminIdAsync();

        Assert.Equal(ProfileError.ReasonRequired, (await _f.Profiles.RejectAsync(admin, instructor.Id, "curto")).Error);
        var rejected = await _f.Profiles.RejectAsync(admin, instructor.Id, "Número da credencial não confere.");
        Assert.True(rejected.Success);
        Assert.Equal(InstructorStatus.Rejected, rejected.Instructor!.Status);

        var resubmitted = await _f.Profiles.UpdateAsync(userId, ValidInput("SP-777002"));
        Assert.True(resubmitted.BackToReview);
        Assert.Equal(InstructorStatus.Pending, resubmitted.Instructor!.Status);
        Assert.Null(resubmitted.Instructor.RejectionReason);
    }

    [Fact]
    public async Task Approved_ChangingCredentialOrCategories_GoesBackToReview_ButPriceDoesNot()
    {
        var userId = await NewInstructorUserAsync();
        var instructor = (await _f.Profiles.SubmitAsync(userId, ValidInput())).Instructor!;
        await _f.Profiles.ApproveAsync(await AdminIdAsync(), instructor.Id);

        var price = await _f.Profiles.UpdateAsync(userId, ValidInput() with { PricePerLesson = 120m, MeetingPoint = "Praça da Lapa" });
        Assert.False(price.BackToReview);
        Assert.Equal(InstructorStatus.Approved, price.Instructor!.Status);
        Assert.Equal(120m, price.Instructor.PricePerLesson);

        var categories = await _f.Profiles.UpdateAsync(userId, ValidInput(cats: LicenseCategory.A | LicenseCategory.B));
        Assert.True(categories.BackToReview);
        Assert.Equal(InstructorStatus.Pending, categories.Instructor!.Status);
    }

    [Fact]
    public async Task Submit_MeetingPointOutsideSaoPaulo_AndOtherInvalidFields_AreRejected()
    {
        var userId = await NewInstructorUserAsync();
        var input = ValidInput() with
        {
            Latitude = -22.9068, Longitude = -43.1729, // Rio de Janeiro
            Categories = LicenseCategory.None, PricePerLesson = 5m, DetranCredential = "x", Neighborhood = "Copacabana"
        };

        var result = await _f.Profiles.SubmitAsync(userId, input);

        Assert.Equal(ProfileError.Invalid, result.Error);
        Assert.Contains("Latitude", result.Errors!.Keys);
        Assert.Contains("Categories", result.Errors.Keys);
        Assert.Contains("PricePerLesson", result.Errors.Keys);
        Assert.Contains("DetranCredential", result.Errors.Keys);
        Assert.Contains("Neighborhood", result.Errors.Keys);
    }

    [Fact]
    public async Task Submit_ByStudentOrTwice_IsRejected()
    {
        Assert.Equal(ProfileError.NotInstructor, (await _f.Profiles.SubmitAsync(1, ValidInput())).Error);

        var userId = await NewInstructorUserAsync();
        Assert.True((await _f.Profiles.SubmitAsync(userId, ValidInput())).Success);
        Assert.Equal(ProfileError.AlreadyExists, (await _f.Profiles.SubmitAsync(userId, ValidInput())).Error);
    }
}
