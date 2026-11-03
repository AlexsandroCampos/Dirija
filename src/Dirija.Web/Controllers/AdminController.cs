using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Web.Infrastructure;
using Dirija.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dirija.Web.Controllers;

/// <summary>Review of instructor registrations. Only admins; there is no public sign up for this role.</summary>
[Route("admin")]
[Authorize(Roles = Roles.Admin)]
public class AdminController(InstructorProfileService profiles, IInstructorRepository instructors, IUserRepository users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? status = null)
    {
        var selected = status switch
        {
            "aprovados" => InstructorStatus.Approved,
            "recusados" => InstructorStatus.Rejected,
            _ => InstructorStatus.Pending
        };
        var list = await instructors.ListByStatusAsync(selected);
        var userMap = (await users.GetByIdsAsync(list.Where(i => i.UserId is not null).Select(i => i.UserId!.Value))).ToDictionary(u => u.Id);
        return View(new AdminListViewModel
        {
            Status = selected,
            Counts = await instructors.CountByStatusAsync(),
            Rows = list.Select(i => new AdminInstructorRow(i, i.UserId is { } id ? userMap.GetValueOrDefault(id) : null)).ToList()
        });
    }

    [HttpGet("instrutores/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var vm = await BuildDetailsAsync(id, null);
        return vm is null ? NotFound() : View(vm);
    }

    [HttpPost("instrutores/{id:int}/aprovar")]
    public async Task<IActionResult> Approve(int id)
    {
        var result = await profiles.ApproveAsync(User.GetUserId(), id);
        if (result.Error == ProfileError.NotFound) return NotFound();
        if (result.Success) TempData["Success"] = $"{result.Instructor!.Name} foi aprovado e já aparece na busca dos alunos.";
        else TempData["Error"] = "Este cadastro não está mais pendente.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("instrutores/{id:int}/recusar")]
    public async Task<IActionResult> Reject(int id, string? motivo)
    {
        var result = await profiles.RejectAsync(User.GetUserId(), id, motivo);
        if (result.Error == ProfileError.NotFound) return NotFound();
        if (result.Error == ProfileError.ReasonRequired)
        {
            ModelState.AddModelError("motivo", $"Explique o motivo para o instrutor ({InstructorProfileService.MinReasonLength} a {InstructorProfileService.MaxReasonLength} caracteres).");
            var vm = await BuildDetailsAsync(id, motivo);
            return vm is null ? NotFound() : View(nameof(Details), vm);
        }
        if (result.Success) TempData["Success"] = $"Cadastro de {result.Instructor!.Name} recusado. O instrutor verá o motivo e poderá corrigir.";
        else TempData["Error"] = "Este cadastro não está mais pendente.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("conta")]
    public async Task<IActionResult> Account()
    {
        var user = await users.GetByIdAsync(User.GetUserId());
        return user is null ? RedirectToAction("Login", "Account") : View(user);
    }

    private async Task<AdminDetailsViewModel?> BuildDetailsAsync(int id, string? reason)
    {
        var instructor = await instructors.GetByIdAsync(id);
        if (instructor?.UserId is not { } userId) return null;
        return new AdminDetailsViewModel { Instructor = instructor, User = await users.GetByIdAsync(userId), RejectReason = reason };
    }
}
