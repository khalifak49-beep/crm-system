using CrmSystem.Infrastructure.Localization;
using CrmSystem.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace CrmSystem.Controllers;

/// <summary>
/// Switches the UI language (English / Arabic) and remembers the choice:
///  - in a cookie for this browser,
///  - in ApplicationUsers.PreferredLanguage for the signed-in user (applied at next login).
/// </summary>
[AllowAnonymous]
public class LanguageController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LanguageController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Set(string lang, string? returnUrl)
    {
        lang = lang == "ar" ? "ar" : "en";
        SetCultureCookie(Response, lang, Request.IsHttps);

        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null && user.PreferredLanguage != lang)
            {
                user.PreferredLanguage = lang;
                user.UpdatedAt = DateTime.UtcNow;
                user.UpdatedBy = user.Id;
                await _userManager.UpdateAsync(user);
                await _signInManager.RefreshSignInAsync(user); // refresh the language claim
            }
        }

        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    public static void SetCultureCookie(HttpResponse response, string lang, bool secure)
    {
        var culture = AppCultures.FromLanguage(lang);
        response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = secure
            });
    }
}
