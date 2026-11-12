using CrmSystem.Data;
using CrmSystem.Infrastructure;
using CrmSystem.Infrastructure.Localization;
using CrmSystem.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CrmSystem.Controllers;

/// <summary>
/// Small base class with the helpers every CRM screen needs
/// (database, current user, settings, translations, access checks, AJAX form results).
/// Not a "pattern" - just shared convenience so each controller stays short.
/// </summary>
public abstract class AppController : Controller
{
    private ApplicationDbContext? _db;
    private ICurrentUser? _user;
    private IStringLocalizer? _l;
    private SettingsSnapshot? _settings;

    protected ApplicationDbContext Db => _db ??= HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
    protected ICurrentUser CurrentUser => _user ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
    protected IStringLocalizer T => _l ??= HttpContext.RequestServices.GetRequiredService<IStringLocalizer<SharedResource>>();
    protected bool IsArabic => AppCultures.IsArabic;
    protected string UserId => CurrentUser.UserId ?? string.Empty;

    protected async Task<SettingsSnapshot> SettingsAsync() =>
        _settings ??= await HttpContext.RequestServices.GetRequiredService<IAppSettings>().GetAsync(HttpContext.RequestAborted);

    /// <summary>Clients the current user may see (see DataScope).</summary>
    protected async Task<IQueryable<Client>> VisibleClientsAsync() =>
        Db.Clients.VisibleTo(CurrentUser, await SettingsAsync());

    protected async Task<bool> CanAccessClientAsync(int clientId) =>
        await (await VisibleClientsAsync()).AnyAsync(c => c.ClientId == clientId);

    /// <summary>Returns the form partial; status 400 when the model is invalid so the JS keeps the modal open.</summary>
    protected IActionResult FormView(string viewName, object model)
    {
        if (!ModelState.IsValid) Response.StatusCode = StatusCodes.Status400BadRequest;
        return PartialView(viewName, model);
    }

    protected IActionResult Saved(string? message = null, object? data = null) =>
        Json(new { ok = true, message = message ?? T["Js.Saved"].Value, data });

    protected IActionResult Fail(string message, int status = StatusCodes.Status400BadRequest)
    {
        Response.StatusCode = status;
        return Json(new { ok = false, message });
    }

    protected IActionResult NotAllowed() => Fail(T["Msg.NotAllowed"], StatusCodes.Status403Forbidden);

    protected IActionResult NotFoundJson() => Fail(T["Msg.NotFound"], StatusCodes.Status404NotFound);

    /// <summary>Base64 RowVersion from a hidden form field -> optimistic concurrency check.</summary>
    protected void SetOriginalRowVersion<TEntity>(TEntity entity, string? rowVersion) where TEntity : class
    {
        if (string.IsNullOrEmpty(rowVersion)) return;
        try { Db.Entry(entity).Property("RowVersion").OriginalValue = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { /* ignore malformed value: normal save */ }
    }
}
