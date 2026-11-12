using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Entities;
using CrmSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// Reports: 9 report types, shared filters, on-screen grid (server paging),
/// Excel export (ClosedXML), print view and PDF (browser "Save as PDF" from the print view,
/// which renders Arabic perfectly without any paid PDF library).
/// </summary>
public class ReportsController : AppController
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports) => _reports = reports;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewBag.Types = await Db.ActivityTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToListAsync();
        return View();
    }

    /// <summary>GET /Reports/Data?type=calls&amp;from=...&amp;page=1 : { columns, items, total, page, pageSize }</summary>
    [HttpGet]
    public async Task<IActionResult> Data(string type, [FromQuery] ReportFilter filter, [FromQuery] GridRequest grid, CancellationToken ct)
    {
        if (!ReportService.Types.Contains(type)) type = "activities";
        var page = await _reports.RunAsync(type, filter, grid, await SettingsAsync(), IsArabic, allRows: false, ct);
        return Json(new
        {
            columns = page.Columns.Select(c => new { key = c.Key, title = T[c.TitleKey].Value, kind = c.Kind }),
            items = page.Items,
            total = page.Total,
            page = page.Page,
            pageSize = page.PageSize
        });
    }

    /// <summary>GET /Reports/Export?type=...  -> .xlsx file</summary>
    [HttpGet]
    public async Task<IActionResult> Export(string type, [FromQuery] ReportFilter filter, [FromQuery] GridRequest grid, CancellationToken ct)
    {
        if (!ReportService.Types.Contains(type)) type = "activities";
        var settings = await SettingsAsync();
        var page = await _reports.RunAsync(type, filter, grid, settings, IsArabic, allRows: true, ct);
        var title = T[$"Reports.Type.{type}"].Value;
        var bytes = ReportService.ToExcel(page, title, await FilterLinesAsync(filter, settings), T, IsArabic, settings.Currency);

        Db.AddAudit(AuditActions.Export, "Report", type, new { type, rows = page.Total, filter.From, filter.To, filter.ClientId, filter.UserId });
        await Db.SaveChangesAsync(ct);

        var fileName = $"{type}-report-{settings.NowLocal:yyyyMMdd-HHmm}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    /// <summary>GET /Reports/Print?type=...&amp;pdf=true : printable page (all rows).</summary>
    [HttpGet]
    public async Task<IActionResult> Print(string type, [FromQuery] ReportFilter filter, [FromQuery] GridRequest grid, bool pdf, CancellationToken ct)
    {
        if (!ReportService.Types.Contains(type)) type = "activities";
        var settings = await SettingsAsync();
        var page = await _reports.RunAsync(type, filter, grid, settings, IsArabic, allRows: true, ct);
        ViewBag.Title = T[$"Reports.Type.{type}"].Value;
        ViewBag.FilterLines = await FilterLinesAsync(filter, settings);
        ViewBag.GeneratedAt = settings.NowLocal;
        ViewBag.Company = settings.CompanyDisplayName(IsArabic);
        ViewBag.Pdf = pdf;
        ViewBag.Currency = settings.Currency;

        Db.AddAudit(AuditActions.Export, "Report", type, new { type, format = pdf ? "pdf" : "print", rows = page.Total });
        await Db.SaveChangesAsync(ct);
        return View(page);
    }

    private async Task<List<string>> FilterLinesAsync(ReportFilter f, Infrastructure.SettingsSnapshot settings)
    {
        var lines = new List<string>
        {
            $"{T["Reports.GeneratedAt"]}: {settings.NowLocal:dd/MM/yyyy hh:mm tt} · {User.Identity?.Name}"
        };
        if (f.From.HasValue || f.To.HasValue)
            lines.Add($"{T["Filter.Period"]}: {f.From?.ToString("dd/MM/yyyy") ?? "…"} → {f.To?.ToString("dd/MM/yyyy") ?? "…"}");
        var arabic = IsArabic;
        if (f.ClientId.HasValue)
            lines.Add($"{T["Field.Company"]}: {await Db.Clients.Where(c => c.ClientId == f.ClientId).Select(c => arabic && c.CompanyNameAr != null ? c.CompanyNameAr : c.CompanyName).FirstOrDefaultAsync()}");
        if (!string.IsNullOrEmpty(f.UserId) && CurrentUser.CanSeeAllData)
            lines.Add($"{T["Reports.Employee"]}: {await Db.Users.Where(u => u.Id == f.UserId).Select(u => u.FullName).FirstOrDefaultAsync()}");
        if (f.ActivityTypeId.HasValue)
            lines.Add($"{T["Field.ActivityType"]}: {await Db.ActivityTypes.Where(t => t.ActivityTypeId == f.ActivityTypeId).Select(t => arabic ? t.NameAr : t.NameEn).FirstOrDefaultAsync()}");
        if (f.FollowUpStatus.HasValue) lines.Add($"{T["Field.Status"]}: {T[$"FollowUpStatus.{(int)f.FollowUpStatus}"]}");
        if (f.ProjectStatus.HasValue) lines.Add($"{T["Field.Status"]}: {T[$"ProjectStatus.{(int)f.ProjectStatus}"]}");
        if (!CurrentUser.CanSeeAllData) lines.Add(T["Reports.OwnDataOnly"]);
        return lines;
    }
}
