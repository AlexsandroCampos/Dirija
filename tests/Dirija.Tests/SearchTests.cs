using Dirija.Core.Entities;
using Dirija.Core.Geo;
using Dirija.Core.Services;

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
}

public class SearchTests
{
    private readonly DomainFixture _f = new();

    [Fact]
    public void Search_DefaultsToPaulista_AndSortsByDistance()
    {
        var result = _f.Search.Search(new SearchQuery());

        Assert.Equal(Neighborhoods.Paulista.Name, result.Origin.Label);
        Assert.NotEmpty(result.Items);
        Assert.Equal(result.Items.Select(i => i.DistanceKm).Order(), result.Items.Select(i => i.DistanceKm));
    }

    [Fact]
    public void Search_RespectsRadius()
    {
        var result = _f.Search.Search(new SearchQuery { Neighborhood = "Moema", RadiusKm = 3 });

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.True(i.DistanceKm <= 3));
        Assert.Contains(result.Items, i => i.Instructor.Neighborhood == "Moema");
    }

    [Fact]
    public void Search_FiltersByCategory()
    {
        var result = _f.Search.Search(new SearchQuery { Category = LicenseCategory.A, RadiusKm = 50 });

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.True(i.Instructor.Categories.HasFlag(LicenseCategory.A)));
    }

    [Fact]
    public void Search_FiltersByMaxPrice()
    {
        var result = _f.Search.Search(new SearchQuery { MaxPrice = 85, RadiusKm = 50 });

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.True(i.Instructor.PricePerLesson <= 85));
    }

    [Fact]
    public void Search_FiltersByMinRating()
    {
        var result = _f.Search.Search(new SearchQuery { MinRating = 4.8, RadiusKm = 50 });

        Assert.NotEmpty(result.Items);
        Assert.All(result.Items, i => Assert.True(i.Instructor.AverageRating >= 4.8));
    }

    [Fact]
    public void Search_SortsByPriceAndRating()
    {
        var byPrice = _f.Search.Search(new SearchQuery { Sort = SearchSort.Price, RadiusKm = 50 }).Items;
        var byRating = _f.Search.Search(new SearchQuery { Sort = SearchSort.Rating, RadiusKm = 50 }).Items;

        Assert.Equal(byPrice.Select(i => i.Instructor.PricePerLesson).Order(), byPrice.Select(i => i.Instructor.PricePerLesson));
        Assert.Equal(byRating.Select(i => i.Instructor.AverageRating).OrderDescending(), byRating.Select(i => i.Instructor.AverageRating));
    }

    [Fact]
    public void Search_NeverListsUnverifiedInstructors()
    {
        var result = _f.Search.Search(new SearchQuery { Neighborhood = "Centro", RadiusKm = 50 });

        Assert.All(result.Items, i => Assert.True(i.Instructor.IsVerified));
        Assert.Contains(_f.Instructors.GetAll(), i => !i.IsVerified); // the seed does contain one
    }

    [Fact]
    public void Search_UsesCoordinatesWhenProvided()
    {
        var result = _f.Search.Search(new SearchQuery { Latitude = -23.5400, Longitude = -46.5760, Neighborhood = "Moema" });

        Assert.Equal("Sua localização", result.Origin.Label);
        Assert.Equal("Tatuapé", result.Items.First().Instructor.Neighborhood);
    }
}

public class SeedTests
{
    private readonly DomainFixture _f = new();

    [Fact]
    public void Seed_HasNoOverlappingSlotsPerInstructor()
    {
        foreach (var group in _f.Store.Slots.Values.GroupBy(s => s.InstructorId))
        {
            var ordered = group.OrderBy(s => s.Start).ToList();
            for (var i = 1; i < ordered.Count; i++)
                Assert.True(ordered[i].Start >= ordered[i - 1].End, $"Overlap for instructor {group.Key}");
        }
    }

    [Fact]
    public void Seed_HasAtLeastTwelveVerifiedInstructorsWithFreeSlots()
    {
        var verified = _f.Instructors.GetAll().Where(i => i.IsVerified).ToList();
        Assert.True(verified.Count >= 12);
        Assert.All(verified, i => Assert.Contains(_f.Store.Slots.Values, s => s.InstructorId == i.Id && s.Status == SlotStatus.Available));
    }

    [Fact]
    public void Seed_DemoStudentHasHistoryAndUpcomingLesson()
    {
        var list = _f.BookingService.ListForStudent(1);
        Assert.Contains(list, b => b.Status == BookingStatus.Completed);
        Assert.Contains(list, b => b.Status == BookingStatus.Confirmed);
    }
}

public class UserRepositoryTests
{
    private readonly DomainFixture _f = new();

    [Fact]
    public void ValidCredentials_ReturnUser() =>
        Assert.Equal(1, _f.Users.ValidateCredentials("ALUNO@dirija.com", "123456")?.Id);

    [Theory]
    [InlineData("aluno@dirija.com", "errada")]
    [InlineData("ninguem@dirija.com", "123456")]
    [InlineData("", "")]
    public void InvalidCredentials_ReturnNull(string email, string password) =>
        Assert.Null(_f.Users.ValidateCredentials(email, password));
}
