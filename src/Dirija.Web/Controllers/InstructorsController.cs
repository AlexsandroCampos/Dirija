using Dirija.Core.Abstractions;
using Dirija.Core.Services;
using Dirija.Core.Time;
using Dirija.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dirija.Web.Controllers;

[Route("instrutores")]
public class InstructorsController(
    IInstructorRepository instructors,
    IReviewRepository reviews,
    BookingService bookingService,
    IClock clock) : Controller
{
    public const int AgendaDays = 7;

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var instructor = await instructors.GetByIdAsync(id);
        if (instructor is null || !instructor.IsVerified) return NotFound();

        // The agenda is organised by São Paulo calendar days; slots are stored in UTC.
        var today = SaoPauloTime.Today(clock.UtcNow);
        var slots = await bookingService.GetBookableSlotsAsync(id, SaoPauloTime.ToUtc(today.AddDays(AgendaDays)));
        var days = Enumerable.Range(0, AgendaDays)
            .Select(d => today.AddDays(d))
            .Select(date => new DaySlots(date, slots.Where(s => SaoPauloTime.ToLocal(s.Start).Date == date).ToList()))
            .ToList();

        return View(new InstructorDetailsViewModel
        {
            Instructor = instructor,
            Reviews = await reviews.GetByInstructorAsync(id),
            Days = days,
            Today = today
        });
    }
}
