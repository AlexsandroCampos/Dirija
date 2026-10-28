using System.ComponentModel.DataAnnotations;
using Dirija.Core.Entities;

namespace Dirija.Web.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Informe sua senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public record DaySlots(DateTime Date, IReadOnlyList<Slot> Slots);

public class InstructorDetailsViewModel
{
    public required Instructor Instructor { get; init; }
    public required IReadOnlyList<Review> Reviews { get; init; }
    public required IReadOnlyList<DaySlots> Days { get; init; }
    public DateTime Today { get; init; }
}

public class CheckoutViewModel
{
    public required Booking Booking { get; init; }
    public required Instructor Instructor { get; init; }
    public required Slot Slot { get; init; }
    public DateTime Now { get; init; }
    public int SecondsLeft => Slot.HeldUntil is { } until ? Math.Max(0, (int)(until - Now).TotalSeconds) : 0;
    public string PixCode { get; init; } = "";
    public PaymentFormModel Form { get; init; } = new();
}

public class PaymentFormModel
{
    public PaymentMethod Method { get; set; } = PaymentMethod.Pix;

    [Display(Name = "Número do cartão")]
    public string? CardNumber { get; set; }

    [Display(Name = "Nome impresso no cartão")]
    public string? CardName { get; set; }

    [Display(Name = "Validade (MM/AA)")]
    public string? CardExpiry { get; set; }

    [Display(Name = "CVV")]
    public string? CardCvv { get; set; }
}

public record BookingListItem(Booking Booking, Instructor Instructor, Slot Slot, bool CanReview, bool HasReview);

public class MyLessonsViewModel
{
    public required IReadOnlyList<BookingListItem> Upcoming { get; init; }
    public required IReadOnlyList<BookingListItem> History { get; init; }
    public DateTime Now { get; init; }
    public string Tab { get; init; } = "proximas";
}

public class ReviewFormModel
{
    [Range(1, 5, ErrorMessage = "Escolha de 1 a 5 estrelas.")]
    public int Rating { get; set; }

    [StringLength(500, ErrorMessage = "Máximo de 500 caracteres.")]
    [Display(Name = "Comentário (opcional)")]
    public string? Comment { get; set; }
}

public class ReviewViewModel
{
    public required Booking Booking { get; init; }
    public required Instructor Instructor { get; init; }
    public required Slot Slot { get; init; }
    public ReviewFormModel Form { get; init; } = new();
}

public class ProfileViewModel
{
    public required User User { get; init; }
    public int UpcomingCount { get; init; }
    public int CompletedCount { get; init; }
}

public class ErrorPageViewModel
{
    public int StatusCode { get; init; }
    public string? RequestId { get; init; }
}
