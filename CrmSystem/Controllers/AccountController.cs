using CrmSystem.Data;
using CrmSystem.Infrastructure.Localization;
using Microsoft.Extensions.Localization;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Identity;
using CrmSystem.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace CrmSystem.Controllers;

/// <summary>
/// Login / logout / access denied.
/// </summary>
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AccountController> _logger;
    private readonly IStringLocalizer<SharedResource> _l;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        ILogger<AccountController> logger,
        IStringLocalizer<SharedResource> localizer)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
        _logger = logger;
        _l = localizer;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToLocal(returnUrl);

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var input = model.UserNameOrEmail.Trim();
        var user = input.Contains('@')
            ? await _userManager.FindByEmailAsync(input)
            : await _userManager.FindByNameAsync(input);

        if (user == null)
        {
            await WriteAuditAsync(AuditActions.LoginFailed, null, input, "Unknown user");
            ModelState.AddModelError(string.Empty, _l["Login.Invalid"]);
            return View(model);
        }

        if (!user.IsActive)
        {
            await WriteAuditAsync(AuditActions.LoginFailed, user.Id, user.UserName, "Inactive account");
            ModelState.AddModelError(string.Empty, _l["Login.Disabled"]);
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            user.LastLoginAtUtc = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            LanguageController.SetCultureCookie(Response, user.PreferredLanguage, Request.IsHttps);
            await WriteAuditAsync(AuditActions.Login, user.Id, user.UserName, null);
            _logger.LogInformation("User {UserName} logged in.", user.UserName);
            return RedirectToLocal(model.ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            await WriteAuditAsync(AuditActions.LoginFailed, user.Id, user.UserName, "Locked out");
            ModelState.AddModelError(string.Empty, _l["Login.LockedOut"]);
            return View(model);
        }

        await WriteAuditAsync(AuditActions.LoginFailed, user.Id, user.UserName, "Wrong password");
        ModelState.AddModelError(string.Empty, _l["Login.Invalid"]);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userId = _userManager.GetUserId(User);
        var userName = User.Identity?.Name;
        await _signInManager.SignOutAsync();
        await WriteAuditAsync(AuditActions.Logout, userId, userName, null);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    /// <summary>Change my own password (popup form from the account menu).</summary>
    [HttpGet]
    public IActionResult ChangePassword() => PartialView("_ChangePassword", new ChangePasswordViewModel());

    [HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (ModelState.IsValid)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();
            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (result.Succeeded)
            {
                await _signInManager.RefreshSignInAsync(user);
                await WriteAuditAsync(AuditActions.PasswordChanged, user.Id, user.UserName, null);
                return Json(new { ok = true, message = _l["Account.PasswordChanged"].Value });
            }
            foreach (var e in result.Errors)
            {
                var translated = _l["Identity." + e.Code];
                ModelState.AddModelError(e.Code == "PasswordMismatch" ? nameof(model.CurrentPassword) : nameof(model.NewPassword),
                    translated.ResourceNotFound ? e.Description : translated.Value);
            }
        }
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return PartialView("_ChangePassword", model);
    }

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Dashboard");

    /// <summary>Login audit is written explicitly because the user is not signed in yet.</summary>
    private async Task WriteAuditAsync(string action, string? userId, string? userName, string? reason)
    {
        try
        {
            var ua = Request.Headers.UserAgent.ToString();
            _db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                UserName = userName?.Length > 256 ? userName[..256] : userName,
                Action = action,
                EntityName = "Account",
                EntityId = userId?.Length > 100 ? userId[..100] : userId,
                NewValues = reason == null ? null : System.Text.Json.JsonSerializer.Serialize(new { Reason = reason }),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = ua.Length > 400 ? ua[..400] : ua,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Never block a login because of the audit log
            _logger.LogError(ex, "Failed to write audit log for {Action}", action);
        }
    }
}
