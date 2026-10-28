using System.Globalization;
using System.Security.Claims;
using Dirija.Core.Entities;
using Dirija.Core.Time;

namespace Dirija.Web.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
}

public static class Ui
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public static string Money(decimal value) => value.ToString("C", PtBr);

    /// <summary>Compact price for map pins, e.g. "R$ 90".</summary>
    public static string ShortMoney(decimal value) =>
        "R$ " + (value % 1 == 0 ? value.ToString("0", PtBr) : value.ToString("0.00", PtBr));

    public static string Rating(double value) => value.ToString("0.0", PtBr);

    public static string Km(double value) => value.ToString("0.0", PtBr) + " km";

    public static string Categories(LicenseCategory c) => c switch
    {
        LicenseCategory.A | LicenseCategory.B => "A e B",
        LicenseCategory.A => "A (moto)",
        LicenseCategory.B => "B (carro)",
        _ => "-"
    };

    public static string DayLabel(DateTime date, DateTime today)
    {
        if (date.Date == today.Date) return "Hoje";
        if (date.Date == today.Date.AddDays(1)) return "Amanhã";
        var name = PtBr.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek).TrimEnd('.');
        return $"{char.ToUpper(name[0])}{name[1..]} {date:dd/MM}";
    }

    // Stored dates are UTC; everything shown to the user is São Paulo local time.

    /// <summary>"segunda-feira, 26 de outubro às 08:00" from a UTC instant.</summary>
    public static string LongDate(DateTime utc) =>
        SaoPauloTime.ToLocal(utc).ToString("dddd, dd 'de' MMMM 'às' HH:mm", PtBr);

    /// <summary>"08:00" from a UTC instant.</summary>
    public static string Time(DateTime utc) => SaoPauloTime.ToLocal(utc).ToString("HH:mm", PtBr);

    /// <summary>"26/10/2026" from a UTC instant.</summary>
    public static string ShortDate(DateTime utc) => SaoPauloTime.ToLocal(utc).ToString("dd/MM/yyyy", PtBr);

    public static string LessonCount(int n) => n == 1 ? "1 aula dada" : $"{n} aulas dadas";
    public static string ReviewCount(int n) => n == 1 ? "1 avaliação" : $"{n} avaliações";

    public static (string Text, string Css) Status(BookingStatus status) => status switch
    {
        BookingStatus.PendingPayment => ("Pagamento pendente", "text-bg-warning"),
        BookingStatus.Confirmed => ("Confirmada", "text-bg-success"),
        BookingStatus.Completed => ("Concluída", "text-bg-secondary"),
        BookingStatus.Canceled => ("Cancelada", "text-bg-danger"),
        _ => (status.ToString(), "text-bg-light")
    };

    /// <summary>Deterministic avatar color per instructor.</summary>
    public static string AvatarColor(int id)
    {
        string[] colors = ["#157a3b", "#0f766e", "#7c5a1e", "#4d7c0f", "#9a3412", "#155e75", "#5b4a8a", "#6b4226"];
        return colors[Math.Abs(id) % colors.Length];
    }
}
