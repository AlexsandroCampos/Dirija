using System.Globalization;
using Dirija.Core.Abstractions;
using Dirija.Core.Services;
using Dirija.Infrastructure;
using Dirija.Infrastructure.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    // Every POST form must carry an antiforgery token.
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

// Render accented pt-BR characters as-is instead of HTML entities (still HTML-encodes <, >, &, quotes).
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

// --- Authentication (cookie, mock users) ---------------------------------------------------
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/conta/entrar";
        o.LogoutPath = "/conta/sair";
        o.AccessDeniedPath = "/conta/acesso-negado";
        o.Cookie.Name = "dirija.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        // HTTPS-only cookie outside local development (localhost runs on plain HTTP).
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(o =>
{
    // Everything requires login unless marked [AllowAnonymous].
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// --- Domain + persistence (EF Core / SQLite) ------------------------------------------------
builder.Services.Configure<BookingOptions>(builder.Configuration.GetSection(BookingOptions.SectionName));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<BookingOptions>>().Value);
builder.Services.Configure<AgendaOptions>(builder.Configuration.GetSection(AgendaOptions.SectionName));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AgendaOptions>>().Value);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddDirijaInfrastructure(SqliteConnectionString);
builder.Services.AddDirijaServices();

var app = builder.Build();

// Migrations, demo data, admin bootstrap and agenda generation.
// Database:ResetOnStartup=true recreates the database (demo reset); Demo:Enabled=false creates no demo data or logins.
// Admin:Email / Admin:Password come from environment variables or user-secrets, never from appsettings.json.
var adminStatus = await DbInitializer.InitializeAsync(app.Services, new InitializerOptions
{
    ResetDatabase = app.Configuration.GetValue<bool>("Database:ResetOnStartup"),
    SeedDemoData = app.Configuration.GetValue("Demo:Enabled", true),
    AdminEmail = app.Configuration["Admin:Email"],
    AdminPassword = app.Configuration["Admin:Password"],
    Agenda = app.Services.GetRequiredService<AgendaOptions>()
});
var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Dirija.Startup");
switch (adminStatus)
{
    case AdminBootstrap.Created: startupLog.LogInformation("Conta de administrador criada a partir da configuração."); break;
    case AdminBootstrap.EmailInUse: startupLog.LogWarning("Admin:Email já pertence a uma conta que não é de administrador; nada foi alterado."); break;
    case AdminBootstrap.WeakPassword: startupLog.LogWarning("Admin:Password não atende à política de senha; o administrador não foi criado."); break;
    case AdminBootstrap.InvalidEmail: startupLog.LogWarning("Admin:Email inválido; o administrador não foi criado."); break;
}
if (app.Configuration.GetValue("Demo:Enabled", true) && !app.Environment.IsDevelopment())
    startupLog.LogWarning("Demo:Enabled=true fora de Development: contas de demonstração com senha conhecida estão ativas.");

var ptBr = new CultureInfo("pt-BR");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(ptBr),
    SupportedCultures = [ptBr],
    SupportedUICultures = [ptBr]
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erro");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/erro/{0}");
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// Resolved lazily (per DbContext options build) so integration tests can override the connection string.
// Relative "Data Source" paths are resolved against the web project's content root.
static string SqliteConnectionString(IServiceProvider sp)
{
    var config = sp.GetRequiredService<IConfiguration>();
    var provider = config["Database:Provider"] ?? "Sqlite";
    if (!provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        throw new NotSupportedException($"Database:Provider '{provider}' ainda não é suportado (use 'Sqlite').");

    var csb = new SqliteConnectionStringBuilder(config.GetConnectionString("Dirija") ?? "Data Source=App_Data/dirija.db");
    if (csb.DataSource != ":memory:" && !Path.IsPathRooted(csb.DataSource))
    {
        csb.DataSource = Path.Combine(sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath, csb.DataSource);
    }
    if (Path.GetDirectoryName(csb.DataSource) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
    return csb.ToString();
}

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
