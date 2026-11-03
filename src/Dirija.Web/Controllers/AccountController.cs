using System.Security.Claims;
using Dirija.Core.Abstractions;
using Dirija.Core.Entities;
using Dirija.Core.Services;
using Dirija.Web.Infrastructure;
using Dirija.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dirija.Web.Controllers;

[Route("conta")]
public class AccountController(IAuthService auth, AccountService accounts, IUserRepository users) : Controller
{
    [AllowAnonymous]
    [HttpGet("entrar")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(returnUrl);
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost("entrar")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await auth.ValidateCredentialsAsync(model.Email, model.Password);
        if (user is null)
        {
            // Generic message: don't reveal whether the e-mail exists or the account is temporarily locked.
            ModelState.AddModelError(string.Empty, "E-mail ou senha incorretos. Após 5 tentativas erradas, o acesso fica bloqueado por 15 minutos.");
            model.Password = "";
            return View(model);
        }

        await SignInAsync(user);
        return RedirectToLocal(model.ReturnUrl, user.Role);
    }

    [AllowAnonymous]
    [HttpGet("criar")]
    public IActionResult Register(string? perfil = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(null);
        return View(new RegisterViewModel { Role = perfil == "instrutor" ? Roles.Instructor : Roles.Student });
    }

    [AllowAnonymous]
    [HttpPost("criar")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (model.Role is not (Roles.Student or Roles.Instructor)) ModelState.AddModelError(nameof(model.Role), "Escolha se você é aluno ou instrutor.");
        if (!ModelState.IsValid) return RegisterView(model);

        var result = await accounts.RegisterAsync(new RegisterInput(
            model.ParsedRole, model.Name, model.Email, model.Phone, model.Neighborhood, model.Password));
        if (!result.Success)
        {
            AddErrors(result.Errors);
            return RegisterView(model);
        }

        await SignInAsync(result.User!);
        if (result.User!.Role == UserRole.Instructor)
        {
            TempData["Success"] = "Conta criada! Agora conte sobre suas aulas para enviarmos seu cadastro para análise.";
            return RedirectToAction("Onboarding", "InstructorArea");
        }
        TempData["Success"] = "Conta criada! Encontre um instrutor perto de você.";
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("dados")]
    public async Task<IActionResult> EditData()
    {
        var user = await users.GetByIdAsync(User.GetUserId());
        if (user is null) return await SignOutToLoginAsync();
        return View(new AccountDataViewModel
        {
            Name = user.Name, Phone = user.Phone, Neighborhood = user.Neighborhood, Email = user.Email,
            IsStudent = user.Role == UserRole.Student
        });
    }

    [HttpPost("dados")]
    public async Task<IActionResult> EditData(AccountDataViewModel model)
    {
        var user = await users.GetByIdAsync(User.GetUserId());
        if (user is null) return await SignOutToLoginAsync();
        model.Email = user.Email;
        model.IsStudent = user.Role == UserRole.Student;
        if (!ModelState.IsValid) return View(model);

        var result = await accounts.UpdateDataAsync(user.Id, model.Name, model.Phone, model.Neighborhood);
        if (!result.Success)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        await SignInAsync(result.User!); // refresh the name shown in the header
        TempData["Success"] = "Dados atualizados.";
        return RedirectToAction(nameof(EditData));
    }

    [HttpGet("senha")]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost("senha")]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(ClearPasswords(model));

        var result = await accounts.ChangePasswordAsync(User.GetUserId(), model.CurrentPassword, model.NewPassword);
        if (!result.Success)
        {
            if (result.Error == AccountError.NotFound) return await SignOutToLoginAsync();
            AddErrors(result.Errors);
            return View(ClearPasswords(model));
        }

        TempData["Success"] = "Senha alterada.";
        return RedirectToLocal(null);
    }

    [AllowAnonymous]
    [HttpGet("acesso-negado")]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    [HttpPost("sair")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    // ------------------------------------------------------------------ helpers

    private async Task SignInAsync(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email),
            // The role never changes for an account; the instructor's approval status is always read from the database.
            new(ClaimTypes.Role, user.Role.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }

    private async Task<IActionResult> SignOutToLoginAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private IActionResult RegisterView(RegisterViewModel model)
    {
        model.Password = "";
        model.ConfirmPassword = "";
        return View(model);
    }

    private static ChangePasswordViewModel ClearPasswords(ChangePasswordViewModel m)
    {
        m.CurrentPassword = m.NewPassword = m.ConfirmNewPassword = "";
        return m;
    }

    private void AddErrors(IReadOnlyDictionary<string, string>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Não foi possível salvar. Revise os dados.");
            return;
        }
        foreach (var (key, message) in errors) ModelState.AddModelError(key, message);
    }

    private IActionResult RedirectToLocal(string? returnUrl, UserRole? role = null)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return Redirect(Ui.HomeFor(role ?? User.GetRole()));
    }
}
