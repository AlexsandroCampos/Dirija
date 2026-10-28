using Dirija.Core.Entities;
using Dirija.Core.Geo;
using Dirija.Core.Services;
using Dirija.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Dirija.Web.Controllers;

[ApiController]
[Route("api")]
public class SearchApiController(InstructorSearchService search) : ControllerBase
{
    public record OriginDto(double Lat, double Lng, string Label);

    public record InstructorDto(
        int Id, string Name, string Initials, string AvatarColor, string Neighborhood,
        decimal Price, string PriceLabel, string PriceShort,
        double Rating, string RatingLabel, int ReviewCount, int LessonsGiven,
        double DistanceKm, string DistanceLabel, string Categories,
        double Lat, double Lng, bool Verified, bool CoversOrigin, string Url);

    public record SearchResponse(OriginDto Origin, double RadiusKm, int Count, IReadOnlyList<InstructorDto> Items);

    /// <summary>
    /// GET /api/instrutores?bairro=Moema&amp;lat=&amp;lng=&amp;raio=10&amp;categoria=B&amp;precoMax=100&amp;notaMin=4&amp;ordem=distancia|preco|nota
    /// </summary>
    [HttpGet("instrutores")]
    public async Task<ActionResult<SearchResponse>> Instructors(
        [FromQuery] string? bairro,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double? raio,
        [FromQuery] string? categoria,
        [FromQuery] decimal? precoMax,
        [FromQuery] double? notaMin,
        [FromQuery] string? ordem)
    {
        var query = new SearchQuery
        {
            Neighborhood = bairro,
            Latitude = lat,
            Longitude = lng,
            RadiusKm = raio ?? InstructorSearchService.DefaultRadiusKm,
            Category = categoria?.Trim().ToUpperInvariant() switch
            {
                "A" => LicenseCategory.A,
                "B" => LicenseCategory.B,
                _ => null
            },
            MaxPrice = precoMax is > 0 ? precoMax : null,
            MinRating = notaMin is > 0 ? notaMin : null,
            Sort = ordem?.Trim().ToLowerInvariant() switch
            {
                "preco" => SearchSort.Price,
                "nota" => SearchSort.Rating,
                _ => SearchSort.Distance
            }
        };

        var result = await search.SearchAsync(query);
        var items = result.Items.Select(x => new InstructorDto(
            x.Instructor.Id, x.Instructor.Name, x.Instructor.Initials, Ui.AvatarColor(x.Instructor.Id),
            x.Instructor.Neighborhood, x.Instructor.PricePerLesson, Ui.Money(x.Instructor.PricePerLesson),
            Ui.ShortMoney(x.Instructor.PricePerLesson), x.Instructor.AverageRating, Ui.Rating(x.Instructor.AverageRating),
            x.Instructor.ReviewCount, x.Instructor.LessonsGiven, x.DistanceKm, Ui.Km(x.DistanceKm),
            Ui.Categories(x.Instructor.Categories), x.Instructor.Latitude, x.Instructor.Longitude,
            x.Instructor.IsVerified, x.CoversOrigin, Url.Action("Details", "Instructors", new { id = x.Instructor.Id })!
        )).ToList();

        return new SearchResponse(
            new OriginDto(result.Origin.Point.Latitude, result.Origin.Point.Longitude, result.Origin.Label),
            Math.Clamp(query.RadiusKm, 0.5, InstructorSearchService.MaxRadiusKm), items.Count, items);
    }

    [HttpGet("bairros")]
    public IEnumerable<string> NeighborhoodNames() => Neighborhoods.All.Select(n => n.Name).Order();
}
