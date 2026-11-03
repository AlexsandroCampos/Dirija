using System.Globalization;
using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Core.Time;
using Dirija.Web.Infrastructure;
using Dirija.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dirija.Web.Controllers;

/// <summary>Instructor area. The approval status is always read from the database, never from the cookie.</summary>
[Route("instrutor")]
[Authorize(Roles = Roles.Instructor)]
public class InstructorAreaController(
    InstructorProfileService profiles,
    AvailabilityService availability,
    BookingService bookingService,
    IUserRepository users,
    ISlotRepository slots,
    IClock clock) : Controller
{
    private const string ErrorKey = "Error";
    private const string SuccessKey = "Success";
    private const int DaysPerWeek = 7;

    // ------------------------------------------------------------------ sign up step 2 / edit professional data

    [HttpGet("cadastro")]
    public async Task<IActionResult> Onboarding()
    {
        var existing = await profiles.GetForUserAsync(User.GetUserId());
        return View(new InstructorOnboardingViewModel
        {
            Existing = existing,
            Form = existing is null ? new InstructorProfileFormModel() : InstructorProfileFormModel.From(existing)
        });
    }

    [HttpPost("cadastro")]
    public async Task<IActionResult> Onboarding([Bind(Prefix = "Form")] InstructorProfileFormModel form)
    {
        var userId = User.GetUserId();
        var existing = await profiles.GetForUserAsync(userId);
        var result = existing is null
            ? await profiles.SubmitAsync(userId, form.ToInput())
            : await profiles.UpdateAsync(userId, form.ToInput());

        if (!result.Success)
        {
            if (result.Errors is { } errors)
                foreach (var (key, message) in errors) ModelState.AddModelError("Form." + key, message);
            else ModelState.AddModelError(string.Empty, "Não foi possível salvar o cadastro.");
            return View(new InstructorOnboardingViewModel { Existing = existing, Form = form });
        }

        TempData[SuccessKey] = existing is null
            ? "Cadastro enviado para análise! Enquanto isso, monte sua disponibilidade semanal."
            : result.BackToReview
                ? "Dados salvos. Seu cadastro voltou para análise da equipe Dirija!."
                : "Dados salvos.";
        return RedirectToAction(existing is null ? nameof(Availability) : nameof(Profile));
    }

    // ------------------------------------------------------------------ agenda

    [HttpGet("")]
    public async Task<IActionResult> Agenda(int semana = 0)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));
        await availability.SyncAsync(instructor.Id);

        var now = clock.UtcNow;
        var today = SaoPauloTime.Today(now);
        var windowDays = availability.Options.WindowDays;
        var maxWeek = Math.Max(0, (windowDays - 1) / DaysPerWeek);
        var week = Math.Clamp(semana, 0, maxWeek);
        var first = today.AddDays(week * DaysPerWeek);
        var dayCount = Math.Min(DaysPerWeek, windowDays - week * DaysPerWeek);

        var entries = await availability.GetEntriesAsync(instructor.Id, first, dayCount);
        var students = (await users.GetByIdsAsync(entries.Where(e => e.Booking is not null).Select(e => e.Booking!.StudentId)))
            .ToDictionary(u => u.Id);
        var days = Enumerable.Range(0, dayCount).Select(d => first.AddDays(d))
            .Select(date => new AgendaDay(date, entries.Where(e => SaoPauloTime.ToLocal(e.Slot.Start).Date == date).ToList()))
            .ToList();

        return View(new InstructorAgendaViewModel
        {
            Instructor = instructor,
            Days = days,
            Students = students,
            HasWeeklyAvailability = (await availability.GetWeeklyAsync(instructor.Id)).Count > 0,
            Week = week,
            MaxWeek = maxWeek,
            Today = today,
            Now = now,
            EarliestHour = availability.Options.EarliestMinute / 60,
            LatestHour = availability.Options.LatestMinute / 60,
            WindowDays = windowDays
        });
    }

    [HttpPost("horarios/{slotId:int}/bloquear")]
    public async Task<IActionResult> BlockSlot(int slotId, int semana = 0)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));
        var result = await availability.BlockSlotAsync(instructor.Id, slotId);
        if (result.Error == AgendaError.NotFound) return NotFound();
        Flash(result, "Horário bloqueado. Ele não aparece mais para os alunos.");
        return RedirectToAction(nameof(Agenda), new { semana });
    }

    [HttpPost("horarios/{slotId:int}/desbloquear")]
    public async Task<IActionResult> UnblockSlot(int slotId, int semana = 0)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));
        var result = await availability.UnblockSlotAsync(instructor.Id, slotId);
        if (result.Error == AgendaError.NotFound) return NotFound();
        Flash(result, "Horário liberado para reservas.");
        return RedirectToAction(nameof(Agenda), new { semana });
    }

    [HttpPost("horarios/extra")]
    public async Task<IActionResult> AddExtraSlot(string? data, int hora, int semana = 0)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));
        if (!DateTime.TryParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            TempData[ErrorKey] = "Escolha a data do horário extra.";
            return RedirectToAction(nameof(Agenda), new { semana });
        }
        var result = await availability.AddExtraSlotAsync(instructor.Id, date.Date.AddHours(hora));
        Flash(result, $"Horário extra aberto em {date:dd/MM} às {Ui.Hour(hora)}.");
        return RedirectToAction(nameof(Agenda), new { semana });
    }

    [HttpPost("horarios/{slotId:int}/remover")]
    public async Task<IActionResult> RemoveExtraSlot(int slotId, int semana = 0)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));
        var result = await availability.RemoveExtraSlotAsync(instructor.Id, slotId);
        if (result.Error == AgendaError.NotFound) return NotFound();
        Flash(result, "Horário extra removido.");
        return RedirectToAction(nameof(Agenda), new { semana });
    }

    // ------------------------------------------------------------------ weekly availability

    [HttpGet("disponibilidade")]
    public async Task<IActionResult> Availability()
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));
        var rules = await availability.GetWeeklyAsync(instructor.Id);
        var rows = WeekOrder.Select(day =>
        {
            var ranges = rules.Where(r => r.DayOfWeek == day).OrderBy(r => r.StartMinute).ToList();
            return new WeeklyDayRow
            {
                Day = day,
                Enabled = ranges.Count > 0,
                Start1 = ranges.ElementAtOrDefault(0)?.StartMinute / 60,
                End1 = ranges.ElementAtOrDefault(0)?.EndMinute / 60,
                Start2 = ranges.ElementAtOrDefault(1)?.StartMinute / 60,
                End2 = ranges.ElementAtOrDefault(1)?.EndMinute / 60
            };
        }).ToList();
        return View(AvailabilityView(rows));
    }

    [HttpPost("disponibilidade")]
    public async Task<IActionResult> Availability(WeeklyAvailabilityViewModel model)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));

        var ranges = new List<AvailabilityRange>();
        foreach (var row in model.Days.Where(r => r.Enabled))
        {
            if (row.Start1 is { } s1 && row.End1 is { } e1) ranges.Add(new AvailabilityRange(row.Day, s1 * 60, e1 * 60));
            else ModelState.AddModelError(string.Empty, $"{Ui.DayName(row.Day)}: informe o início e o fim do primeiro período.");
            if (row.Start2 is { } s2 && row.End2 is { } e2) ranges.Add(new AvailabilityRange(row.Day, s2 * 60, e2 * 60));
            else if (row.Start2 is not null || row.End2 is not null)
                ModelState.AddModelError(string.Empty, $"{Ui.DayName(row.Day)}: complete o segundo período ou deixe-o em branco.");
        }

        if (ModelState.IsValid && availability.ValidateRanges(ranges) != AgendaError.None)
            ModelState.AddModelError(string.Empty, "Cada período precisa terminar depois de começar, entre " +
                $"{Ui.Hour(availability.Options.EarliestMinute / 60)} e {Ui.Hour(availability.Options.LatestMinute / 60)}, sem sobrepor o outro período do mesmo dia.");
        if (!ModelState.IsValid) return View(AvailabilityView(model.Days));

        await availability.SaveWeeklyAsync(instructor.Id, ranges);
        TempData[SuccessKey] = ranges.Count == 0
            ? "Disponibilidade salva sem horários: alunos não verão horários livres."
            : $"Disponibilidade salva. Seus horários dos próximos {availability.Options.WindowDays} dias foram atualizados; aulas já reservadas não mudaram.";
        return RedirectToAction(nameof(Agenda));
    }

    // ------------------------------------------------------------------ time offs (exceptions)

    [HttpGet("bloqueios")]
    public async Task<IActionResult> TimeOff(string? data = null)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));
        var form = new TimeOffFormModel();
        if (DateTime.TryParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) form.StartDate = d;
        return View(await TimeOffViewAsync(instructor, form));
    }

    [HttpPost("bloqueios")]
    public async Task<IActionResult> TimeOff(TimeOffFormModel form)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));

        if (form.StartDate is not { } startDate)
        {
            ModelState.AddModelError(nameof(form.StartDate), "Escolha a data.");
            return View(await TimeOffViewAsync(instructor, form));
        }
        var endDate = form.EndDate ?? startDate;
        if (endDate < startDate)
        {
            ModelState.AddModelError(nameof(form.EndDate), "A data final precisa ser igual ou depois da inicial.");
            return View(await TimeOffViewAsync(instructor, form));
        }
        var localStart = form.AllDay ? startDate.Date : startDate.Date.AddHours(form.StartHour);
        var localEnd = form.AllDay ? endDate.Date.AddDays(1) : endDate.Date.AddHours(form.EndHour);
        if (localEnd <= localStart)
        {
            ModelState.AddModelError(nameof(form.EndHour), "O fim precisa ser depois do início.");
            return View(await TimeOffViewAsync(instructor, form));
        }
        var startUtc = SaoPauloTime.ToUtc(localStart);
        var endUtc = SaoPauloTime.ToUtc(localEnd);

        // Lessons already booked in the period are never canceled silently: the instructor decides.
        var affected = await availability.GetAffectedBookingsAsync(instructor.Id, startUtc, endUtc);
        if (affected.Count > 0 && form.Decision is not ("cancel" or "keep"))
            return View(await TimeOffViewAsync(instructor, form, affected));

        var reason = (form.CancelReason ?? "").Trim();
        if (affected.Count > 0 && form.Decision == "cancel" && reason.Length is < 3 or > BookingService.MaxReasonLength)
        {
            ModelState.AddModelError(nameof(form.CancelReason), "Escreva o motivo que o aluno vai ver (3 a 100 caracteres).");
            return View(await TimeOffViewAsync(instructor, form, affected));
        }

        var result = await availability.AddTimeOffAsync(instructor.Id, startUtc, endUtc, form.Note);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, AgendaMessage(result.Error));
            return View(await TimeOffViewAsync(instructor, form, affected));
        }

        var canceled = 0;
        if (form.Decision == "cancel")
        {
            foreach (var (booking, _) in affected)
                if ((await bookingService.CancelByInstructorAsync(booking.Id, instructor.Id, reason, blockSlot: false)).Success) canceled++;
        }

        TempData[SuccessKey] = canceled > 0
            ? $"Período bloqueado e {canceled} aula(s) cancelada(s). Os alunos verão o motivo e receberão reembolso integral (simulado)."
            : affected.Count > 0 ? "Período bloqueado. As aulas já reservadas foram mantidas." : "Período bloqueado.";
        return RedirectToAction(nameof(TimeOff));
    }

    [HttpPost("bloqueios/{id:int}/remover")]
    public async Task<IActionResult> RemoveTimeOff(int id)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));
        var result = await availability.RemoveTimeOffAsync(instructor.Id, id);
        if (result.Error == AgendaError.NotFound) return NotFound();
        Flash(result, "Bloqueio removido. Os horários do período voltaram a ficar livres.");
        return RedirectToAction(nameof(TimeOff));
    }

    // ------------------------------------------------------------------ lessons

    [HttpGet("aulas")]
    public async Task<IActionResult> Lessons(string? aba = null)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));

        var now = clock.UtcNow;
        var list = (await bookingService.ListForInstructorAsync(instructor.Id))
            .Where(b => b.CanceledBy != CanceledBy.System) // expired checkouts are noise for the instructor
            .ToList();
        var slotMap = (await slots.GetByIdsAsync(list.Select(b => b.SlotId))).ToDictionary(s => s.Id);
        var students = (await users.GetByIdsAsync(list.Select(b => b.StudentId))).ToDictionary(u => u.Id);

        var items = list.Where(b => slotMap.ContainsKey(b.SlotId))
            .Select(b =>
            {
                var slot = slotMap[b.SlotId];
                return new InstructorLessonItem(b, slot, students.GetValueOrDefault(b.StudentId),
                    ShowContact: b.Status == BookingStatus.Confirmed && slot.Start > now);
            }).ToList();
        var upcoming = items.Where(i => i.Booking.Status is BookingStatus.Confirmed or BookingStatus.PendingPayment && i.Slot.End > now)
            .OrderBy(i => i.Slot.Start).ToList();
        var history = items.Except(upcoming).OrderByDescending(i => i.Slot.Start).ToList();

        return View(new InstructorLessonsViewModel
        {
            Upcoming = upcoming, History = history, Now = now, Tab = aba == "historico" ? "historico" : "proximas"
        });
    }

    [HttpPost("aulas/{id:int}/cancelar")]
    public async Task<IActionResult> CancelLesson(int id, string? motivo)
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));
        var result = await bookingService.CancelByInstructorAsync(id, instructor.Id, motivo);
        if (result.Error is BookingError.BookingNotFound or BookingError.NotOwner) return NotFound();

        if (result.Success) TempData[SuccessKey] = "Aula cancelada. O aluno verá o motivo e receberá reembolso integral (simulado); o horário ficou bloqueado.";
        else TempData[ErrorKey] = result.Error == BookingError.InvalidReason
            ? "Escreva o motivo do cancelamento (3 a 100 caracteres). O aluno vai ver esse texto."
            : "Essa aula não pode mais ser cancelada.";
        return RedirectToAction(nameof(Lessons));
    }

    // ------------------------------------------------------------------ profile

    [HttpGet("perfil")]
    public async Task<IActionResult> Profile()
    {
        if (await CurrentInstructorAsync() is not { } instructor) return RedirectToAction(nameof(Onboarding));
        var user = await users.GetByIdAsync(User.GetUserId());
        if (user is null) return RedirectToAction("Login", "Account");
        return View(new InstructorProfilePageViewModel { User = user, Instructor = instructor });
    }

    // ------------------------------------------------------------------ helpers

    private static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private Task<Instructor?> CurrentInstructorAsync() => profiles.GetForUserAsync(User.GetUserId());

    private WeeklyAvailabilityViewModel AvailabilityView(List<WeeklyDayRow> rows) => new()
    {
        Days = rows,
        EarliestHour = availability.Options.EarliestMinute / 60,
        LatestHour = availability.Options.LatestMinute / 60
    };

    private async Task<TimeOffViewModel> TimeOffViewAsync(Instructor instructor, TimeOffFormModel form,
        IReadOnlyList<(Booking Booking, Slot Slot)>? affected = null)
    {
        var list = affected ?? [];
        var students = (await users.GetByIdsAsync(list.Select(a => a.Booking.StudentId))).ToDictionary(u => u.Id);
        return new TimeOffViewModel
        {
            Form = form,
            Upcoming = await availability.GetUpcomingTimeOffsAsync(instructor.Id),
            Affected = list.Select(a => new AffectedLesson(a.Booking, a.Slot, students.GetValueOrDefault(a.Booking.StudentId))).ToList()
        };
    }

    private void Flash(AgendaResult result, string success)
    {
        if (result.Success) TempData[SuccessKey] = success;
        else TempData[ErrorKey] = AgendaMessage(result.Error);
    }

    private string AgendaMessage(AgendaError error) => error switch
    {
        AgendaError.InPast => "Esse horário já passou.",
        AgendaError.OutsideWindow => $"Só é possível abrir horários nos próximos {availability.Options.WindowDays} dias.",
        AgendaError.TooLong => $"Um bloqueio pode durar no máximo {availability.Options.MaxTimeOffDays} dias.",
        AgendaError.NoteTooLong => $"A anotação pode ter no máximo {AvailabilityService.MaxNoteLength} caracteres.",
        AgendaError.InvalidRange => "Período inválido: confira datas e horários.",
        AgendaError.SlotBusy => "Esse horário tem uma reserva. Para liberá-lo, cancele a aula em \"Aulas\".",
        AgendaError.AlreadyBlocked => "Esse horário já está bloqueado.",
        AgendaError.CoveredByPeriod => "Esse horário faz parte de um bloqueio maior. Remova o bloqueio em \"Bloqueios\".",
        AgendaError.AlreadyExists => "Já existe um horário nesse dia e hora.",
        AgendaError.NotManual => "Só horários extras podem ser removidos; use \"Bloquear\" para os demais.",
        AgendaError.Conflict => "Um aluno acabou de reservar um horário desse período. Confira a agenda e tente de novo.",
        _ => "Não foi possível concluir a operação."
    };
}
