using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Core.Time;
using Dirija.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dirija.Web.Controllers;

[Route("instrutores")]
[Authorize(Roles = Roles.Student)]
public class InstructorsController(
    IInstructorRepository instructors,
    IReviewRepository reviews,
    BookingService bookingService,
    AvailabilityService availability,
    IClock clock) : Controller
{
    public const int DaysPerWeek = 7;

    /// <summary>Instructor profile with the agenda, one week at a time (?semana=0 is the current week).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, int semana = 0)
    {
        var instructor = await instructors.GetByIdAsync(id);
        if (instructor is null || !instructor.IsApproved) return NotFound();

        // Keeps the rolling window of a real agenda filled (cheap when nothing changed).
        if (!instructor.IsDemo) await availability.SyncAsync(id);

        var windowDays = instructor.IsDemo ? Dirija.Infrastructure.Seed.SeedData.DaysAhead : availability.Options.WindowDays;
        var maxWeek = Math.Max(0, (windowDays - 1) / DaysPerWeek);
        var week = Math.Clamp(semana, 0, maxWeek);

        // The agenda is organised by São Paulo calendar days; slots are stored in UTC.
        var today = SaoPauloTime.Today(clock.UtcNow);
        var first = today.AddDays(week * DaysPerWeek);
        var dayCount = Math.Min(DaysPerWeek, windowDays - week * DaysPerWeek);
        var slots = await bookingService.GetBookableSlotsAsync(id, SaoPauloTime.ToUtc(first.AddDays(dayCount)));
        var days = Enumerable.Range(0, dayCount)
            .Select(d => first.AddDays(d))
            .Select(date => new DaySlots(date, slots.Where(s => SaoPauloTime.ToLocal(s.Start).Date == date).ToList()))
            .ToList();

        return View(new InstructorDetailsViewModel
        {
            Instructor = instructor,
            Reviews = await reviews.GetByInstructorAsync(id),
            Days = days,
            Today = today,
            Week = week,
            MaxWeek = maxWeek,
            MinNoticeHours = bookingService.Options.MinNoticeHours,
            FullRefundHours = bookingService.Options.StudentFullRefundHours
        });
    }
}
