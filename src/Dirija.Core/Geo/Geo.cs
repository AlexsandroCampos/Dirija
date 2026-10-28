using System.Globalization;
using System.Text;

namespace Dirija.Core.Geo;

public readonly record struct GeoPoint(double Latitude, double Longitude);

public static class GeoMath
{
    private const double EarthRadiusKm = 6371.0088;

    /// <summary>Great-circle distance between two points using the Haversine formula.</summary>
    public static double HaversineKm(GeoPoint a, GeoPoint b)
    {
        static double Rad(double deg) => deg * Math.PI / 180.0;

        var dLat = Rad(b.Latitude - a.Latitude);
        var dLng = Rad(b.Longitude - a.Longitude);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(Rad(a.Latitude)) * Math.Cos(Rad(b.Latitude)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}

public record Neighborhood(string Name, GeoPoint Location);

/// <summary>
/// Static list of São Paulo neighborhoods used as a lightweight geocoder for the demo
/// (no external geocoding API). Coordinates are approximate centroids.
/// </summary>
public static class Neighborhoods
{
    public static readonly Neighborhood Paulista = new("Av. Paulista", new(-23.5614, -46.6559));

    public static readonly IReadOnlyList<Neighborhood> All = new List<Neighborhood>
    {
        Paulista,
        new("Bela Vista", new(-23.5580, -46.6450)),
        new("Butantã", new(-23.5720, -46.7080)),
        new("Centro", new(-23.5505, -46.6333)),
        new("Ipiranga", new(-23.5890, -46.6070)),
        new("Itaim Bibi", new(-23.5850, -46.6800)),
        new("Jabaquara", new(-23.6460, -46.6410)),
        new("Lapa", new(-23.5270, -46.7040)),
        new("Moema", new(-23.6010, -46.6650)),
        new("Mooca", new(-23.5600, -46.5990)),
        new("Perdizes", new(-23.5360, -46.6780)),
        new("Pinheiros", new(-23.5670, -46.6930)),
        new("Santana", new(-23.5020, -46.6250)),
        new("Saúde", new(-23.6180, -46.6390)),
        new("Tatuapé", new(-23.5400, -46.5760)),
        new("Vila Madalena", new(-23.5530, -46.6910)),
        new("Vila Mariana", new(-23.5890, -46.6340)),
    };

    /// <summary>Case and accent insensitive lookup ("tatuape" finds "Tatuapé").</summary>
    public static Neighborhood? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = Normalize(name);
        return All.FirstOrDefault(n => Normalize(n.Name) == key)
               ?? All.FirstOrDefault(n => Normalize(n.Name).Contains(key));
    }

    public static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
