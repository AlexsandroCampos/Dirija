using Dirija.Core.Entities;
using Dirija.Core.Geo;
using Dirija.Core.Services;
using Dirija.Core.Time;
using Dirija.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace Dirija.Tests;

public class GeoTests
{
    [Fact]
    public void Haversine_OneDegreeOfLongitudeAtEquator_IsAbout111Km()
    {
        var d = GeoMath.HaversineKm(new GeoPoint(0, 0), new GeoPoint(0, 1));
        Assert.InRange(d, 111.1, 111.3);
    }

    [Fact]
    public void Haversine_SaoPauloToRio_IsAbout357Km()
    {
        var sp = new GeoPoint(-23.5505, -46.6333);
        var rio = new GeoPoint(-22.9068, -43.1729);
        Assert.InRange(GeoMath.HaversineKm(sp, rio), 352, 362);
    }

    [Fact]
    public void Haversine_SamePoint_IsZero() =>
        Assert.Equal(0, GeoMath.HaversineKm(Neighborhoods.Paulista.Location, Neighborhoods.Paulista.Location), 6);

    [Theory]
    [InlineData("tatuape", "Tatuapé")]
    [InlineData("  MOEMA ", "Moema")]
    [InlineData("vila mad", "Vila Madalena")]
    public void Neighborhoods_Find_IsCaseAndAccentInsensitive(string input, string expected) =>
        Assert.Equal(expected, Neighborhoods.Find(input)?.Name);

    [Fact]
    public void Neighborhoods_Find_UnknownReturnsNull() => Assert.Null(Neighborhoods.Find("Copacabana"));

    [Fact]
    public void SaoPauloTime_IsUtcMinus3()
    {
        var utc = new DateTime(2026, 10, 26, 11, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 10, 26, 8, 0, 0), SaoPauloTime.ToLocal(utc));
        Assert.Equal(utc, SaoPauloTime.ToUtc(new DateTime(2026, 10, 26, 8, 0, 0)));
    }
}

public class SearchTests : IAsyncLifetime
{
    private DomainFixture _f = null!;
    public async Task InitializeAsync() => _f = await DomainFixture.CreateAsync();
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact]
    public async Task Search_DefaultsToPaulista_AndSortsByDistance()
    {
        var result = await _f.Search.SearchAsync(new SearchQuery());

        Assert.Equal(Neighborhoods.Paulista.Name, result.Origin.Label);
        Assert.NotEmpty(result.Items);
        Assert.Equal(result.Items.Select(i => i.DistanceKm).Order(), result.Items.Select(i => i.DistanceKm));
    }

    [Fact]
    public async Task Search_RespectsRadius()
    {
        var result = await _f.Search.SearchAsync(new SearchQuery { Neighborhood = "Moema", RadiusKm = 3 });

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.True(i.DistanceKm <= 3));
        Assert.Contains(result.Items, i => i.Instructor.Neighborhood == "Moema");
    }

    [Fact]
    public async Task Search_FiltersByCategory()
    {
        var result = await _f.Search.SearchAsync(new SearchQuery { Category = LicenseCategory.A, RadiusKm = 50 });

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.True(i.Instructor.Categories.HasFlag(LicenseCategory.A)));
    }

    [Fact]
    public async Task Search_FiltersByMaxPrice()
    {
        var result = await _f.Search.SearchAsync(new SearchQuery { MaxPrice = 85, RadiusKm = 50 });

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.True(i.Instructor.PricePerLesson <= 85));
    }

    [Fact]
    public async Task Search_FiltersByMinRating()
    {
        var result = await _f.Search.SearchAsync(new SearchQuery { MinRating = 4.8, RadiusKm = 50 });

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.True(i.Instructor.AverageRating >= 4.8));
    }

    [Fact]
    public async Task Search_SortsByPriceAndRating()
    {
        var byPrice = (await _f.Search.SearchAsync(new SearchQuery { Sort = SearchSort.Price, RadiusKm = 50 })).Items;
        var byRating = (await _f.Search.SearchAsync(new SearchQuery { Sort = SearchSort.Rating, RadiusKm = 50 })).Items;

        Assert.Equal(byPrice.Select(i => i.Instructor.PricePerLesson).Order(), byPrice.Select(i => i.Instructor.PricePerLesson));
        Assert.Equal(byRating.Select(i => i.Instructor.AverageRating).OrderDescending(), byRating.Select(i => i.Instructor.AverageRating));
    }

    [Fact]
    public async Task Search_NeverListsUnverifiedInstructors()
    {
        var result = await _f.Search.SearchAsync(new SearchQuery { Neighborhood = "Centro", RadiusKm = 50 });

        Assert.All(result.Items, i => Assert.True(i.Instructor.IsVerified));
        Assert.True(await _f.Db.Instructors.AnyAsync(i => !i.IsVerified)); // the seed does contain one
    }

    [Fact]
    public async Task Search_UsesCoordinatesWhenProvided()
    {
        var result = await _f.Search.SearchAsync(new SearchQuery { Latitude = -23.5400, Longitude = -46.5760, Neighborhood = "Moema" });

        Assert.Equal("Sua localização", result.Origin.Label);
        Assert.Equal("Tatuapé", result.Items.First().Instructor.Neighborhood);
    }
}

public class SeedTests : IAsyncLifetime
{
    private DomainFixture _f = null!;
    public async Task InitializeAsync() => _f = await DomainFixture.CreateAsync();
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact]
    public async Task Seed_HasNoOverlappingSlotsPerInstructor()
    {
        var slots = await _f.Db.Slots.ToListAsync();
        foreach (var group in slots.GroupBy(s => s.InstructorId))
        {
            var ordered = group.OrderBy(s => s.Start).ToList();
            for (var i = 1; i < ordered.Count; i++)
                Assert.True(ordered[i].Start >= ordered[i - 1].End, $"Overlap for instructor {group.Key}");
        }
    }

    [Fact]
    public async Task Seed_HasAtLeastTwelveVerifiedInstructorsWithFreeSlots()
    {
        var verified = await _f.Db.Instructors.Where(i => i.IsVerified).Select(i => i.Id).ToListAsync();
        Assert.True(verified.Count >= 12);
        foreach (var id in verified)
            Assert.True(await _f.Db.Slots.AnyAsync(s => s.InstructorId == id && s.Status == SlotStatus.Available));
    }

    [Fact]
    public async Task Seed_DemoStudentHasHistoryAndUpcomingLesson()
    {
        var list = await _f.BookingService.ListForStudentAsync(1);
        Assert.Contains(list, b => b.Status == BookingStatus.Completed);
        Assert.Contains(list, b => b.Status == BookingStatus.Confirmed);
    }

    [Fact]
    public async Task Seed_SlotsAreUtcAndBetween8And17InSaoPaulo()
    {
        var slots = await _f.Db.Slots.AsNoTracking().ToListAsync();
        Assert.All(slots, s =>
        {
            Assert.Equal(DateTimeKind.Utc, s.Start.Kind);
            Assert.InRange(SaoPauloTime.ToLocal(s.Start).Hour, 8, 17);
        });
    }

    [Fact]
    public async Task Seed_StoresPasswordsAsHash()
    {
        var user = await _f.Db.Users.SingleAsync(u => u.Email == SeedData.DemoStudentEmail);
        Assert.NotEqual(SeedData.DemoPassword, user.PasswordHash);
        Assert.DoesNotContain(SeedData.DemoPassword, user.PasswordHash);
    }
}

public class AuthServiceTests : IAsyncLifetime
{
    private DomainFixture _f = null!;
    public async Task InitializeAsync() => _f = await DomainFixture.CreateAsync();
    public async Task DisposeAsync() => await _f.DisposeAsync();

    [Fact]
    public async Task ValidCredentials_ReturnUser_CaseInsensitiveEmail() =>
        Assert.Equal(1, (await _f.Auth.ValidateCredentialsAsync("  ALUNO@dirija.com ", "123456"))?.Id);

    [Theory]
    [InlineData("aluno@dirija.com", "errada")]
    [InlineData("ninguem@dirija.com", "123456")]
    [InlineData("", "")]
    public async Task InvalidCredentials_ReturnNull(string email, string password) =>
        Assert.Null(await _f.Auth.ValidateCredentialsAsync(email, password));
}
