using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Web.Infrastructure;
using Dirija.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dirija.Web.Controllers;

[Route("perfil")]
public class ProfileController(IUserRepository users, BookingService bookingService) : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        var user = users.GetById(User.GetUserId());
        if (user is null) return RedirectToAction("Login", "Account");

        var bookings = bookingService.ListForStudent(user.Id);
        return View(new ProfileViewModel
        {
            User = user,
            UpcomingCount = bookings.Count(b => b.Status is BookingStatus.Confirmed or BookingStatus.PendingPayment),
            CompletedCount = bookings.Count(b => b.Status == BookingStatus.Completed)
        });
    }
}
