using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Dirija.Core.Entities;
using Dirija.Infrastructure.Persistence;
using Dirija.Infrastructure.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Dirija.Tests;

public static partial class Http
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    public static HttpClient NewClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public static async Task<string> TokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var match = TokenRegex().Match(html);
        Assert.True(match.Success, $"Antiforgery token not found in {url}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string formUrl, string postUrl, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await TokenAsync(client, formUrl);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = SeedData.DemoPassword) =>
        await PostAsync(client, "/conta/entrar", "/conta/entrar", new() { ["Email"] = email, ["Password"] = password });

    /// <summary>Path (and query) of the redirect; the cookie handler sends absolute URLs for login/access denied.</summary>
    public static string Location(HttpResponseMessage res) => res.Headers.Location switch
    {
        null => "",
        { IsAbsoluteUri: true } uri => uri.PathAndQuery,
        var uri => uri.OriginalString
    };
}

public class RoleIntegrationTests : IClassFixture<DirijaWebFactory>
{
    private readonly DirijaWebFactory _factory;
    public RoleIntegrationTests(DirijaWebFactory factory) => _factory = factory;

    private Dictionary<string, string> SignUp(string role, string email) => new()
    {
        ["Role"] = role, ["Name"] = "Pessoa Teste", ["Email"] = email, ["Phone"] = "(11) 98888-7777",
        ["Neighborhood"] = "Pinheiros", ["Password"] = "senhaboa1", ["ConfirmPassword"] = "senhaboa1", ["AcceptTerms"] = "true"
    };

    [Theory]
    [InlineData(SeedData.DemoStudentEmail, "/")]
    [InlineData(SeedData.DemoInstructorEmail, "/instrutor")]
    [InlineData(SeedData.DemoAdminEmail, "/admin")]
    public async Task Login_SendsEachProfileToItsStartPage(string email, string expected)
    {
        var res = await Http.LoginAsync(Http.NewClient(_factory), email);

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal(expected, Http.Location(res));
    }

    [Fact]
    public async Task SignUp_Student_LogsInAndCanSearch()
    {
        var client = Http.NewClient(_factory);

        var res = await Http.PostAsync(client, "/conta/criar", "/conta/criar", SignUp(Roles.Student, "aluno.novo@dirija.com"));

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/", Http.Location(res));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/instrutores?bairro=Pinheiros")).StatusCode);
    }

    [Fact]
    public async Task SignUp_DuplicateEmail_ShowsErrorAndDoesNotLogIn()
    {
        var client = Http.NewClient(_factory);

        var res = await Http.PostAsync(client, "/conta/criar", "/conta/criar", SignUp(Roles.Student, "ALUNO@dirija.com"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Já existe uma conta com este e-mail", await res.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/perfil")).StatusCode);
    }

    [Fact]
    public async Task SignUp_CannotCreateAnAdmin()
    {
        var client = Http.NewClient(_factory);

        var res = await Http.PostAsync(client, "/conta/criar", "/conta/criar", SignUp("Admin", "falso.admin@dirija.com"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);
    }

    [Fact]
    public async Task SignUp_Instructor_GoesToStep2_ThenSubmitsForReview()
    {
        var client = Http.NewClient(_factory);
        var res = await Http.PostAsync(client, "/conta/criar", "/conta/criar", SignUp(Roles.Instructor, "instrutor.novo@dirija.com"));
        Assert.Equal("/instrutor/cadastro", Http.Location(res));

        // Without step 2 the instructor area always leads back to it.
        Assert.Equal("/instrutor/cadastro", Http.Location(await client.GetAsync("/instrutor")));

        var step2 = await Http.PostAsync(client, "/instrutor/cadastro", "/instrutor/cadastro", new()
        {
            ["Form.DetranCredential"] = "SP-990011", ["Form.CategoryB"] = "true", ["Form.PricePerLesson"] = "95",
            ["Form.Neighborhood"] = "Lapa", ["Form.Latitude"] = "-23.527000", ["Form.Longitude"] = "-46.704000",
            ["Form.ServiceRadiusKm"] = "8", ["Form.Vehicle"] = "Onix 2022", ["Form.HasDualControl"] = "true",
            ["Form.MeetingPoint"] = "Estação Lapa (CPTM)", ["Form.Bio"] = "Aulas com calma."
        });
        Assert.Equal("/instrutor/disponibilidade", Http.Location(step2));
        Assert.Contains("Cadastro em análise", await client.GetStringAsync("/instrutor"));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DirijaDbContext>();
        var saved = await db.Instructors.SingleAsync(i => i.DetranCredential == "SP-990011");
        Assert.Equal(InstructorStatus.Pending, saved.Status);
        Assert.Equal(-23.527, saved.Latitude, 3); // parsed with the invariant culture despite the pt-BR request culture
    }

    [Theory]
    [InlineData(SeedData.DemoStudentEmail, "/instrutor")]
    [InlineData(SeedData.DemoStudentEmail, "/admin")]
    [InlineData(SeedData.DemoInstructorEmail, "/minhas-aulas")]
    [InlineData(SeedData.DemoInstructorEmail, "/api/instrutores")]
    [InlineData(SeedData.DemoInstructorEmail, "/admin")]
    [InlineData(SeedData.DemoAdminEmail, "/instrutores/1")]
    [InlineData(SeedData.DemoAdminEmail, "/instrutor/aulas")]
    public async Task EachProfile_IsDeniedTheOtherProfilesPages(string email, string url)
    {
        var client = Http.NewClient(_factory);
        await Http.LoginAsync(client, email);

        var res = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.StartsWith("/conta/acesso-negado", Http.Location(res));
    }

    [Fact]
    public async Task Instructor_Agenda_ShowsTheBookedStudent_AndLessonsShowContact()
    {
        var client = Http.NewClient(_factory);
        await Http.LoginAsync(client, SeedData.DemoInstructorEmail);

        var lessons = await client.GetStringAsync("/instrutor/aulas");

        Assert.Contains("Aluna Demo 2", lessons);
        Assert.Contains("(11) 90000-0002", lessons);   // confirmed and upcoming: contact is shown
        Assert.Contains("R$ 76,50", lessons);          // instructor's amount after the 15% fee
    }

    [Fact]
    public async Task Instructor_CannotCancelAnotherInstructorsBooking()
    {
        var client = Http.NewClient(_factory);
        await Http.LoginAsync(client, SeedData.DemoInstructorEmail);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DirijaDbContext>();
        var other = await db.Bookings.FirstAsync(b => b.InstructorId != 1);

        var res = await Http.PostAsync(client, "/instrutor/aulas", $"/instrutor/aulas/{other.Id}/cancelar", new() { ["motivo"] = "teste de acesso" });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

/// <summary>Separate app instance: these tests change shared state (approve the pending instructor).</summary>
public class AdminFlowTests : IClassFixture<DirijaWebFactory>
{
    private readonly DirijaWebFactory _factory;
    public AdminFlowTests(DirijaWebFactory factory) => _factory = factory;

    private record SearchDto(int Count, List<ItemDto> Items);
    private record ItemDto(int Id, string Name);

    [Fact]
    public async Task Admin_ApprovesPendingInstructor_ThenStudentsFindHim()
    {
        var student = Http.NewClient(_factory);
        await Http.LoginAsync(student, SeedData.DemoStudentEmail);
        var before = await student.GetFromJsonAsync<SearchDto>("/api/instrutores?bairro=Centro&raio=50");
        Assert.DoesNotContain(before!.Items, i => i.Name == "Tiago Ramos");

        var admin = Http.NewClient(_factory);
        await Http.LoginAsync(admin, SeedData.DemoAdminEmail);
        Assert.Contains("Tiago Ramos", await admin.GetStringAsync("/admin"));

        // Rejecting without a reason is refused and keeps the registration pending.
        var noReason = await Http.PostAsync(admin, "/admin/instrutores/13", "/admin/instrutores/13/recusar", new() { ["motivo"] = "" });
        Assert.Equal(HttpStatusCode.OK, noReason.StatusCode);
        Assert.Contains("Explique o motivo", await noReason.Content.ReadAsStringAsync());

        var approve = await Http.PostAsync(admin, "/admin/instrutores/13", "/admin/instrutores/13/aprovar", new());
        Assert.Equal("/admin", Http.Location(approve));

        var after = await student.GetFromJsonAsync<SearchDto>("/api/instrutores?bairro=Centro&raio=50");
        Assert.Contains(after!.Items, i => i.Name == "Tiago Ramos");
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync("/instrutores/13")).StatusCode);
    }
}

public class ConfiguredAdminFactory : DirijaWebFactory
{
    protected override IReadOnlyDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        ["Demo:Enabled"] = "false",
        ["Admin:Email"] = "chefe@dirija.com",
        ["Admin:Password"] = "SenhaForte123"
    };
}

public class ConfiguredAdminTests : IClassFixture<ConfiguredAdminFactory>
{
    private readonly ConfiguredAdminFactory _factory;
    public ConfiguredAdminTests(ConfiguredAdminFactory factory) => _factory = factory;

    [Fact]
    public async Task AdminFromConfiguration_CanLogIn_AndDemoDisabled_CreatesNoDemoAccounts()
    {
        var admin = Http.NewClient(_factory);
        var login = await Http.LoginAsync(admin, "chefe@dirija.com", "SenhaForte123");
        Assert.Equal("/admin", Http.Location(login));

        var demo = await Http.LoginAsync(Http.NewClient(_factory), SeedData.DemoStudentEmail);
        Assert.Equal(HttpStatusCode.OK, demo.StatusCode); // login page again: the demo account does not exist

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DirijaDbContext>();
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(0, await db.Instructors.CountAsync());
    }
}

public class AdminBootstrapTests
{
    [Fact]
    public async Task WeakConfiguredPassword_CreatesNoAdmin()
    {
        await using var f = await DomainFixture.CreateAsync();
        await using var db = f.NewContext();

        var status = await DbInitializer.InitializeAsync(db, f.Clock, DomainFixture.Hasher,
            new InitializerOptions { AdminEmail = "fraco@dirija.com", AdminPassword = "123", Agenda = DomainFixture.Agenda });

        Assert.Equal(AdminBootstrap.WeakPassword, status);
        Assert.False(await db.Users.AnyAsync(u => u.Email == "fraco@dirija.com"));
    }

    [Fact]
    public async Task ConfiguredEmailOfAStudent_IsNotTurnedIntoAdmin()
    {
        await using var f = await DomainFixture.CreateAsync();
        await using var db = f.NewContext();

        var status = await DbInitializer.InitializeAsync(db, f.Clock, DomainFixture.Hasher,
            new InitializerOptions { AdminEmail = SeedData.DemoStudentEmail, AdminPassword = "SenhaForte123", Agenda = DomainFixture.Agenda });

        Assert.Equal(AdminBootstrap.EmailInUse, status);
        Assert.Equal(UserRole.Student, (await db.Users.SingleAsync(u => u.Email == SeedData.DemoStudentEmail)).Role);
    }
}

public class MigrationTests
{
    /// <summary>A database created before this release (IsVerified column) is upgraded without losing data.</summary>
    [Fact]
    public async Task OldDatabase_IsUpgraded_IsVerifiedBecomesStatus_AndDataIsKept()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dirija-mig-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<DirijaDbContext>().UseSqlite($"Data Source={path}").Options;
        try
        {
            await using (var db = new DirijaDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync("20261028005450_Inicial");
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO users (Id, Name, Email, Phone, PasswordHash, Role, Neighborhood) VALUES (1, 'Aluno Antigo', 'antigo@dirija.com', '', 'x', 'Student', 'Moema');
                    INSERT INTO instructors (Id, Name, Bio, Categories, PricePerLesson, Neighborhood, Latitude, Longitude, ServiceRadiusKm, AverageRating, ReviewCount, LessonsGiven, Vehicle, HasDualControl, IsVerified, DetranCredential, MeetingPoint, LessonMinutes)
                        VALUES (1, 'Aprovado Antigo', '', 2, 90, 'Moema', -23.6, -46.66, 8, 4.5, 1, 1, 'Carro', 1, 1, 'SP-1', 'Praça', 50),
                               (2, 'Pendente Antigo', '', 2, 80, 'Moema', -23.6, -46.66, 8, 0, 0, 0, 'Carro', 1, 0, '', 'Praça', 50);
                    INSERT INTO slots (Id, InstructorId, Start, End, Status, Version) VALUES (1, 1, '2026-11-02 13:00:00', '2026-11-02 13:50:00', 'Booked', 1);
                    INSERT INTO bookings (Id, StudentId, InstructorId, SlotId, Price, PlatformFee, InstructorAmount, Status, PaymentMethod, CreatedAt, CancelReason, Version)
                        VALUES (1, 1, 1, 1, 90, 13.5, 76.5, 'Canceled', 'Pix', '2026-10-20 10:00:00', 'Cancelada pelo aluno', 0);
                    """);
            }

            await using (var db = new DirijaDbContext(options))
            {
                await db.Database.MigrateAsync();
                var instructors = await db.Instructors.OrderBy(i => i.Id).ToListAsync();
                Assert.Equal(InstructorStatus.Approved, instructors[0].Status);
                Assert.Equal(InstructorStatus.Pending, instructors[1].Status);
                Assert.All(instructors, i => Assert.True(i.IsDemo));
                Assert.True((await db.Slots.SingleAsync()).IsActive);
                Assert.Equal(CanceledBy.Student, (await db.Bookings.SingleAsync()).CanceledBy);
                Assert.Equal("antigo@dirija.com", (await db.Users.SingleAsync()).Email);
            }
        }
        finally
        {
            TempDb.Delete(path);
        }
    }
}
