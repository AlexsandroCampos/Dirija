using System.Diagnostics;
using Dirija.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dirija.Web.Controllers;

public class HomeController : Controller
{
    /// <summary>Main screen: map search for instructors.</summary>
    [HttpGet("")]
    public IActionResult Index() => View();

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Route("erro/{code:int?}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(int? code = null)
    {
        var status = code ?? 500;
        Response.StatusCode = status;
        return View(new ErrorPageViewModel
        {
            StatusCode = status,
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
