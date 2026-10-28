namespace Dirija.Core.Time;

/// <summary>
/// Dates are stored in UTC; this converts to/from São Paulo local time for display and for
/// business rules expressed in local hours (e.g. lessons between 08:00 and 17:00).
/// </summary>
public static class SaoPauloTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        // Brazil has no daylight saving time since 2019.
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília");
    }

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    /// <summary>Local calendar date in São Paulo for the given UTC instant.</summary>
    public static DateTime Today(DateTime utcNow) => ToLocal(utcNow).Date;
}
