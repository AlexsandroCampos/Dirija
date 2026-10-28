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
        o.AccessDeniedPath = "/conta/entrar";
        o.Cookie.Name = "dirija.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
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
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddDirijaInfrastructure(SqliteConnectionString);
builder.Services.AddScoped<InstructorSearchService>();
builder.Services.AddScoped<BookingService>();

var app = builder.Build();

// Migrations, first-run seed and agenda top-up. Database:ResetOnStartup=true recreates the database (demo reset).
await DbInitializer.InitializeAsync(app.Services, app.Configuration.GetValue<bool>("Database:ResetOnStartup"));

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
