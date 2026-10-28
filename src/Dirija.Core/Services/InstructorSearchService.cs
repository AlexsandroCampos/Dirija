using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Geo;

namespace Dirija.Core.Services;

public enum SearchSort
{
    Distance,
    Price,
    Rating
}

public record SearchQuery
{
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? Neighborhood { get; init; }
    public double RadiusKm { get; init; } = InstructorSearchService.DefaultRadiusKm;
    public LicenseCategory? Category { get; init; }
    public decimal? MaxPrice { get; init; }
    public double? MinRating { get; init; }
    public SearchSort Sort { get; init; } = SearchSort.Distance;
}

public record SearchOrigin(GeoPoint Point, string Label);

public record InstructorSearchItem(Instructor Instructor, double DistanceKm, bool CoversOrigin);

public record SearchResult(SearchOrigin Origin, IReadOnlyList<InstructorSearchItem> Items);

public class InstructorSearchService(IInstructorRepository instructors)
{
    public const double DefaultRadiusKm = 10;
    public const double MaxRadiusKm = 50;

    public SearchResult Search(SearchQuery query)
    {
        var origin = ResolveOrigin(query);
        var radius = Math.Clamp(query.RadiusKm, 0.5, MaxRadiusKm);

        var items = instructors.GetAll()
            .Where(i => i.IsVerified) // only audited credentials are listed
            .Where(i => query.Category is null or LicenseCategory.None || i.Categories.HasFlag(query.Category.Value))
            .Where(i => query.MaxPrice is null || i.PricePerLesson <= query.MaxPrice)
            .Where(i => query.MinRating is null || i.AverageRating >= query.MinRating)
            .Select(i =>
            {
                var d = GeoMath.HaversineKm(origin.Point, new GeoPoint(i.Latitude, i.Longitude));
                return new InstructorSearchItem(i, Math.Round(d, 1), d <= i.ServiceRadiusKm);
            })
            .Where(x => x.DistanceKm <= radius);

        items = query.Sort switch
        {
            SearchSort.Price => items.OrderBy(x => x.Instructor.PricePerLesson).ThenBy(x => x.DistanceKm),
            SearchSort.Rating => items.OrderByDescending(x => x.Instructor.AverageRating).ThenBy(x => x.DistanceKm),
            _ => items.OrderBy(x => x.DistanceKm)
        };

        return new SearchResult(origin, items.ToList());
    }

    public static SearchOrigin ResolveOrigin(SearchQuery query)
    {
        if (query.Latitude is { } lat && query.Longitude is { } lng &&
            lat is >= -90 and <= 90 && lng is >= -180 and <= 180)
        {
            return new SearchOrigin(new GeoPoint(lat, lng), "Sua localização");
        }

        var hood = Neighborhoods.Find(query.Neighborhood) ?? Neighborhoods.Paulista;
        return new SearchOrigin(hood.Location, hood.Name);
    }
}
