using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Dirija.Tests;

public partial class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IntegrationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    private static async Task<string> GetTokenAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var match = TokenRegex().Match(html);
        Assert.True(match.Success, $"Antiforgery token not found in {url}");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, string token, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpClient> LoginAsync(HttpClient client, string email)
    {
        var token = await GetTokenAsync(client, "/conta/entrar");
        var res = await PostFormAsync(client, "/conta/entrar", token, new() { ["Email"] = email, ["Password"] = "123456" });
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        return client;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/minhas-aulas")]
    [InlineData("/perfil")]
    [InlineData("/api/instrutores")]
    public async Task ProtectedRoutes_WithoutCookie_RedirectToLogin(string url)
    {
        var res = await NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.StartsWith("/conta/entrar", res.Headers.Location!.PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShowsGenericError()
    {
        var client = NewClient();
        var token = await GetTokenAsync(client, "/conta/entrar");
        var res = await PostFormAsync(client, "/conta/entrar", token, new() { ["Email"] = "aluno@dirija.com", ["Password"] = "x" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("E-mail ou senha incorretos", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Header_IsHiddenOnLogin_AndShownAfterLogin()
    {
        var client = NewClient();
        Assert.DoesNotContain("app-header", await client.GetStringAsync("/conta/entrar"));

        await LoginAsync(client, "aluno@dirija.com");
        Assert.Contains("app-header", await client.GetStringAsync("/perfil"));
    }

    [Fact]
    public async Task Login_PostWithoutAntiforgeryToken_IsRejected()
    {
        var res = await NewClient().PostAsync("/conta/entrar",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "aluno@dirija.com", ["Password"] = "123456" }));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task SearchApi_ReturnsFilteredJson()
    {
        var client = await LoginAsync(NewClient(), "aluno@dirija.com");

        var data = await client.GetFromJsonAsync<SearchResponseDto>("/api/instrutores?bairro=Moema&categoria=B&precoMax=100&raio=20");

        Assert.NotNull(data);
        Assert.Equal("Moema", data!.Origin.Label);
        Assert.NotEmpty(data.Items);
        Assert.Equal(data.Items.Count, data.Count);
        Assert.All(data.Items, i =>
        {
            Assert.True(i.Price <= 100);
            Assert.Contains("B", i.Categories);
            Assert.StartsWith("R$", i.PriceShort);
            Assert.StartsWith("/instrutores/", i.Url);
        });
        Assert.Equal(data.Items.Select(i => i.DistanceKm).Order(), data.Items.Select(i => i.DistanceKm));
    }

    [Fact]
    public async Task InstructorDetails_ValidAndInvalidIds()
    {
        var client = await LoginAsync(NewClient(), "aluno@dirija.com");

        var ok = await client.GetAsync("/instrutores/1");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Carlos Mendes", await ok.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/instrutores/9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/instrutores/13")).StatusCode); // unverified
    }

    [GeneratedRegex("name=\"slotId\" value=\"(\\d+)\"")]
    private static partial Regex SlotRegex();

    [Fact]
    public async Task Smoke_FullStudentFlow_SearchReservePayAndSeeLesson()
    {
        var client = await LoginAsync(NewClient(), "aluno@dirija.com");
        var other = await LoginAsync(NewClient(), "aluna2@dirija.com");

        var search = await client.GetFromJsonAsync<SearchResponseDto>("/api/instrutores?bairro=Pinheiros");
        var instructorUrl = search!.Items.First().Url;

        var detailsHtml = await client.GetStringAsync(instructorUrl);
        var slotId = SlotRegex().Match(detailsHtml).Groups[1].Value;
        var token = TokenRegex().Match(detailsHtml).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(slotId));

        // Reserve (hold)
        var reserve = await PostFormAsync(client, "/reservas", WebUtility.HtmlDecode(token), new() { ["slotId"] = slotId });
        Assert.Equal(HttpStatusCode.Redirect, reserve.StatusCode);
        var checkoutUrl = reserve.Headers.Location!.OriginalString;
        Assert.Matches(@"^/reservas/\d+/pagamento$", checkoutUrl);

        // Another student cannot take the same slot (anti-overbooking) nor see the booking.
        var otherToken = await GetTokenAsync(other, instructorUrl);
        var otherReserve = await PostFormAsync(other, "/reservas", otherToken, new() { ["slotId"] = slotId });
        Assert.Equal(instructorUrl, otherReserve.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(checkoutUrl)).StatusCode);

        // Pay with a (fake) card
        var payToken = await GetTokenAsync(client, checkoutUrl);
        var pay = await PostFormAsync(client, checkoutUrl, payToken, new()
        {
            ["Method"] = "CreditCard", ["CardNumber"] = "4111 1111 1111 1111", ["CardName"] = "ALUNO DEMO",
            ["CardExpiry"] = "12/35", ["CardCvv"] = "123"
        });
        Assert.Equal(HttpStatusCode.Redirect, pay.StatusCode);
        var confirmedUrl = pay.Headers.Location!.OriginalString;
        Assert.EndsWith("/confirmada", confirmedUrl);

        var confirmedHtml = await client.GetStringAsync(confirmedUrl);
        Assert.Contains("Aula confirmada", confirmedHtml);
        Assert.Contains("Cartão final 1111", confirmedHtml);
        Assert.DoesNotContain("4111 1111 1111 1111", confirmedHtml);

        var lessons = await client.GetStringAsync("/minhas-aulas");
        Assert.Contains("Confirmada", lessons);
        Assert.Contains(search.Items.First().Name, lessons);
    }

    [Fact]
    public async Task Pay_WithInvalidCard_ShowsErrorsAndDoesNotEchoCardNumber()
    {
        var client = await LoginAsync(NewClient(), "aluna2@dirija.com");
        var detailsHtml = await client.GetStringAsync("/instrutores/6");
        var slotId = SlotRegex().Match(detailsHtml).Groups[1].Value;
        var token = WebUtility.HtmlDecode(TokenRegex().Match(detailsHtml).Groups[1].Value);
        var reserve = await PostFormAsync(client, "/reservas", token, new() { ["slotId"] = slotId });
        var checkoutUrl = reserve.Headers.Location!.OriginalString;

        var payToken = await GetTokenAsync(client, checkoutUrl);
        var res = await PostFormAsync(client, checkoutUrl, payToken, new()
        {
            ["Method"] = "CreditCard", ["CardNumber"] = "1234 5678 9012 3456", ["CardName"] = "X", ["CardExpiry"] = "13/20", ["CardCvv"] = "1"
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Número de cartão inválido", html);
        Assert.Contains("Validade inválida", html);
        Assert.DoesNotContain("1234 5678 9012 3456", html);
    }

    [Fact]
    public async Task ErrorPage_ForUnknownRoute_IsFriendly404()
    {
        var client = await LoginAsync(NewClient(), "aluno@dirija.com");
        var res = await client.GetAsync("/nao-existe");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Contains("Página não encontrada", await res.Content.ReadAsStringAsync());
    }

    private record OriginDto(double Lat, double Lng, string Label);
    private record ItemDto(int Id, string Name, decimal Price, string PriceShort, double DistanceKm, string Categories, string Url);
    private record SearchResponseDto(OriginDto Origin, double RadiusKm, int Count, List<ItemDto> Items);
}
