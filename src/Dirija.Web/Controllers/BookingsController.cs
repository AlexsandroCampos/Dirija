using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Web.Infrastructure;
using Dirija.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dirija.Web.Controllers;

public class BookingsController(
    BookingService bookingService,
    IInstructorRepository instructors,
    ISlotRepository slots,
    IClock clock) : Controller
{
    private const string ErrorKey = "Error";
    private const string SuccessKey = "Success";

    // ---------------------------------------------------------------- reserve (hold)

    [HttpPost("reservas")]
    public IActionResult Reserve(int slotId)
    {
        var slot = slots.GetById(slotId);
        if (slot is null) return NotFound();

        var result = bookingService.HoldSlot(User.GetUserId(), slotId);
        if (!result.Success)
        {
            TempData[ErrorKey] = result.Error switch
            {
                BookingError.SlotUnavailable => "Esse horário acabou de ser reservado por outra pessoa. Escolha outro horário.",
                BookingError.SlotInPast => "Esse horário já passou. Escolha outro horário.",
                _ => "Não foi possível reservar esse horário."
            };
            return RedirectToAction("Details", "Instructors", new { id = slot.InstructorId });
        }

        return RedirectToAction(nameof(Checkout), new { id = result.Booking!.Id });
    }

    // ---------------------------------------------------------------- checkout / payment (simulated)

    [HttpGet("reservas/{id:int}/pagamento")]
    public IActionResult Checkout(int id)
    {
        var result = bookingService.GetForStudent(id, User.GetUserId());
        if (!result.Success) return NotFound();
        var booking = result.Booking!;

        if (booking.Status == BookingStatus.Confirmed) return RedirectToAction(nameof(Confirmed), new { id });
        if (booking.Status != BookingStatus.PendingPayment)
        {
            TempData[ErrorKey] = "O tempo para pagamento dessa reserva expirou. Escolha o horário novamente.";
            return RedirectToAction("Details", "Instructors", new { id = booking.InstructorId });
        }

        return View(BuildCheckout(booking, new PaymentFormModel()));
    }

    [HttpPost("reservas/{id:int}/pagamento")]
    public IActionResult Pay(int id, PaymentFormModel form)
    {
        var owned = bookingService.GetForStudent(id, User.GetUserId());
        if (!owned.Success) return NotFound();

        string label;
        if (form.Method == PaymentMethod.CreditCard)
        {
            ValidateCard(form);
            if (!ModelState.IsValid)
            {
                if (owned.Booking!.Status != BookingStatus.PendingPayment) return RedirectToAction(nameof(Checkout), new { id });
                // Never echo the card number/CVV back to the page.
                form.CardNumber = null;
                form.CardCvv = null;
                return View("Checkout", BuildCheckout(owned.Booking!, form));
            }
            var digits = OnlyDigits(form.CardNumber);
            label = $"Cartão final {digits[^4..]}";
        }
        else
        {
            form.Method = PaymentMethod.Pix;
            label = "PIX";
        }

        var result = bookingService.ConfirmPayment(id, User.GetUserId(), form.Method, label);
        if (!result.Success)
        {
            TempData[ErrorKey] = result.Error == BookingError.HoldExpired
                ? "O tempo para pagamento expirou e o horário foi liberado. Escolha o horário novamente."
                : "Não foi possível confirmar o pagamento.";
            return RedirectToAction("Details", "Instructors", new { id = owned.Booking!.InstructorId });
        }

        return RedirectToAction(nameof(Confirmed), new { id });
    }

    [HttpGet("reservas/{id:int}/confirmada")]
    public IActionResult Confirmed(int id)
    {
        var result = bookingService.GetForStudent(id, User.GetUserId());
        if (!result.Success) return NotFound();
        var booking = result.Booking!;
        if (booking.Status is not (BookingStatus.Confirmed or BookingStatus.Completed))
            return RedirectToAction(nameof(Checkout), new { id });

        return View(BuildCheckout(booking, new PaymentFormModel()));
    }

    // ---------------------------------------------------------------- my lessons

    [HttpGet("minhas-aulas")]
    public IActionResult Index(string? aba = null)
    {
        var now = clock.Now;
        var items = bookingService.ListForStudent(User.GetUserId())
            .Select(b =>
            {
                var hasReview = bookingService.HasReview(b.Id);
                return new BookingListItem(b, instructors.GetById(b.InstructorId)!, slots.GetById(b.SlotId)!,
                    b.Status == BookingStatus.Completed && !hasReview, hasReview);
            })
            .ToList();

        var upcoming = items
            .Where(i => i.Booking.Status is BookingStatus.Confirmed or BookingStatus.PendingPayment && i.Slot.End > now)
            .OrderBy(i => i.Slot.Start).ToList();
        var history = items.Except(upcoming).OrderByDescending(i => i.Slot.Start).ToList();

        return View(new MyLessonsViewModel
        {
            Upcoming = upcoming,
            History = history,
            Now = now,
            Tab = aba == "historico" ? "historico" : "proximas"
        });
    }

    [HttpPost("reservas/{id:int}/cancelar")]
    public IActionResult Cancel(int id)
    {
        var result = bookingService.Cancel(id, User.GetUserId());
        if (result.Error is BookingError.BookingNotFound or BookingError.NotOwner) return NotFound();

        if (result.Success) TempData[SuccessKey] = "Aula cancelada. O horário foi liberado para outros alunos.";
        else TempData[ErrorKey] = "Essa aula não pode mais ser cancelada.";
        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------- review

    [HttpGet("reservas/{id:int}/avaliar")]
    public IActionResult Review(int id)
    {
        var vm = BuildReview(id, new ReviewFormModel());
        if (vm is null) return NotFound();
        if (vm.Booking.Status != BookingStatus.Completed || bookingService.HasReview(id))
        {
            TempData[ErrorKey] = "Essa aula não pode ser avaliada.";
            return RedirectToAction(nameof(Index), new { aba = "historico" });
        }
        return View(vm);
    }

    [HttpPost("reservas/{id:int}/avaliar")]
    public IActionResult Review(int id, ReviewFormModel form)
    {
        if (!ModelState.IsValid)
        {
            var vm = BuildReview(id, form);
            return vm is null ? NotFound() : View(vm);
        }

        var result = bookingService.AddReview(id, User.GetUserId(), User.Identity?.Name ?? "Aluno", form.Rating, form.Comment);
        if (result.Error is BookingError.BookingNotFound or BookingError.NotOwner) return NotFound();

        if (result.Success) TempData[SuccessKey] = "Obrigado! Sua avaliação foi publicada.";
        else TempData[ErrorKey] = result.Error == BookingError.AlreadyReviewed
            ? "Você já avaliou essa aula."
            : "Essa aula não pode ser avaliada.";
        return RedirectToAction(nameof(Index), new { aba = "historico" });
    }

    // ---------------------------------------------------------------- helpers

    private CheckoutViewModel BuildCheckout(Booking booking, PaymentFormModel form) => new()
    {
        Booking = booking,
        Instructor = instructors.GetById(booking.InstructorId)!,
        Slot = slots.GetById(booking.SlotId)!,
        Now = clock.Now,
        Form = form,
        // MOCK: looks like a PIX "copia e cola" code but is NOT a valid payload (no real payment happens).
        PixCode = $"00020126360014BR.GOV.BCB.PIX0114DIRIJA{booking.Id:D8}5204000053039865406{booking.Price:0.00}5802BR6009SAO PAULO6304A1B2"
            .Replace(',', '.')
    };

    private ReviewViewModel? BuildReview(int id, ReviewFormModel form)
    {
        var result = bookingService.GetForStudent(id, User.GetUserId());
        if (!result.Success) return null;
        var b = result.Booking!;
        return new ReviewViewModel
        {
            Booking = b,
            Instructor = instructors.GetById(b.InstructorId)!,
            Slot = slots.GetById(b.SlotId)!,
            Form = form
        };
    }

    private void ValidateCard(PaymentFormModel form)
    {
        var digits = OnlyDigits(form.CardNumber);
        if (digits.Length is < 13 or > 19 || !PassesLuhn(digits))
            ModelState.AddModelError(nameof(form.CardNumber), "Número de cartão inválido.");

        if (string.IsNullOrWhiteSpace(form.CardName) || form.CardName.Trim().Length < 3)
            ModelState.AddModelError(nameof(form.CardName), "Informe o nome impresso no cartão.");

        var expiry = (form.CardExpiry ?? "").Trim();
        var validExpiry = false;
        if (expiry.Length == 5 && expiry[2] == '/' &&
            int.TryParse(expiry[..2], out var month) && int.TryParse(expiry[3..], out var year) && month is >= 1 and <= 12)
        {
            var lastDay = new DateTime(2000 + year, month, 1).AddMonths(1);
            validExpiry = lastDay > clock.Now;
        }
        if (!validExpiry) ModelState.AddModelError(nameof(form.CardExpiry), "Validade inválida (use MM/AA).");

        var cvv = OnlyDigits(form.CardCvv);
        if (cvv.Length is < 3 or > 4 || cvv.Length != (form.CardCvv ?? "").Trim().Length)
            ModelState.AddModelError(nameof(form.CardCvv), "CVV inválido.");
    }

    private static string OnlyDigits(string? value) =>
        new((value ?? "").Where(char.IsAsciiDigit).ToArray());

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var alt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alt) { n *= 2; if (n > 9) n -= 9; }
            sum += n;
            alt = !alt;
        }
        return sum % 10 == 0;
    }
}
