using System.Globalization;
using Dirija.Core.Abstractions;
using Dirija.Core.InMemory;
using Dirija.Core.Services;
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

// --- Domain (in-memory; swap for EF Core repositories later) --------------------------------
builder.Services.Configure<BookingOptions>(builder.Configuration.GetSection(BookingOptions.SectionName));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<BookingOptions>>().Value);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(sp => SeedData.Create(sp.GetRequiredService<IClock>().Now));
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
builder.Services.AddSingleton<IInstructorRepository, InMemoryInstructorRepository>();
builder.Services.AddSingleton<ISlotRepository, InMemorySlotRepository>();
builder.Services.AddSingleton<IBookingRepository, InMemoryBookingRepository>();
builder.Services.AddSingleton<IReviewRepository, InMemoryReviewRepository>();
builder.Services.AddSingleton<InstructorSearchService>();
builder.Services.AddSingleton<BookingService>();

var app = builder.Build();

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

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
