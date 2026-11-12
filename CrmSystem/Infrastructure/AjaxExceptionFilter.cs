using CrmSystem.Infrastructure.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CrmSystem.Infrastructure;

/// <summary>
/// Turns exceptions thrown during AJAX requests into a clean JSON message
/// (shown as a toast) instead of an HTML error page. Everything is logged.
///   - concurrency conflict  -> 409 "changed by another user"
///   - unique index violated -> 409 "already exists"
///   - foreign key violated  -> 409 "record is in use"
///   - anything else         -> 500 generic message
/// </summary>
public sealed class AjaxExceptionFilter : IExceptionFilter
{
    private readonly ILogger<AjaxExceptionFilter> _logger;
    private readonly IStringLocalizer<SharedResource> _l;

    public AjaxExceptionFilter(ILogger<AjaxExceptionFilter> logger, IStringLocalizer<SharedResource> l)
    {
        _logger = logger;
        _l = l;
    }

    public void OnException(ExceptionContext context)
    {
        var request = context.HttpContext.Request;
        var isAjax = request.Headers.XRequestedWith == "XMLHttpRequest"
                     || request.Headers.Accept.ToString().Contains("application/json");
        if (!isAjax) return; // normal pages use the standard error page

        int status;
        string message;
        switch (context.Exception)
        {
            case DbUpdateConcurrencyException:
                status = StatusCodes.Status409Conflict;
                message = _l["Msg.ConcurrencyConflict"];
                break;
            case DbUpdateException { InnerException: SqlException sql } when sql.Number is 2601 or 2627:
                status = StatusCodes.Status409Conflict;
                message = _l["Msg.Duplicate"];
                break;
            case DbUpdateException { InnerException: SqlException sql } when sql.Number == 547:
                status = StatusCodes.Status409Conflict;
                message = _l["Msg.InUse"];
                break;
            default:
                status = StatusCodes.Status500InternalServerError;
                message = _l["Js.Error.Generic"];
                break;
        }

        _logger.LogError(context.Exception, "AJAX request failed: {Method} {Path}", request.Method, request.Path);
        context.Result = new JsonResult(new { ok = false, message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
