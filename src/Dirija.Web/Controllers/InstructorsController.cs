using Dirija.Core.Abstractions;
using Dirija.Core.Services;
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
    public IActionResult Details(int id)
    {
        var instructor = instructors.GetById(id);
        if (instructor is null || !instructor.IsVerified) return NotFound();

        var today = clock.Now.Date;
        var slots = bookingService.GetBookableSlots(id, today.AddDays(AgendaDays));
        var days = Enumerable.Range(0, AgendaDays)
            .Select(d => today.AddDays(d))
            .Select(date => new DaySlots(date, slots.Where(s => s.Start.Date == date).ToList()))
            .ToList();

        return View(new InstructorDetailsViewModel
        {
            Instructor = instructor,
            Reviews = reviews.GetByInstructor(id),
            Days = days,
            Today = today
        });
    }
}
