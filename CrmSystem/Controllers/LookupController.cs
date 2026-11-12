using CrmSystem.Infrastructure;
using CrmSystem.Models.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// JSON lookups for searchable drop-downs (Tom Select), always filtered by the
/// current user's data scope. Every endpoint supports ?search= and ?id= (selected item).
/// Item shape: { id, name, code?, extra? }
/// </summary>
public class LookupController : AppController
{
    private static int Take(int take) => Math.Clamp(take, 1, 100);

    [HttpGet]
    public async Task<IActionResult> Clients(string? search, int? id, int take = 50, bool includeArchived = false, CancellationToken ct = default)
    {
        var arabic = IsArabic;
        var query = (await VisibleClientsAsync()).AsNoTracking();
        if (id.HasValue) query = query.Where(c => c.ClientId == id.Value);
        else
        {
            if (!includeArchived) query = query.Where(c => c.Status != Models.Enums.ClientStatus.Archived);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(c => c.CompanyName.Contains(s) || (c.CompanyNameAr != null && c.CompanyNameAr.Contains(s)) || c.CompanyCode.Contains(s));
            }
        }
        var items = await query.OrderBy(c => c.CompanyName).Take(Take(take))
            .Select(c => new { id = c.ClientId, code = c.CompanyCode, name = arabic && c.CompanyNameAr != null ? c.CompanyNameAr : c.CompanyName, extra = c.City })
            .ToListAsync(ct);
        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> Contacts(int? clientId, string? search, int? id, int take = 50, CancellationToken ct = default)
    {
        var visible = (await VisibleClientsAsync()).Select(c => c.ClientId);
        var query = Db.Contacts.AsNoTracking().Where(c => visible.Contains(c.ClientId));
        if (id.HasValue) query = query.Where(c => c.ContactId == id.Value);
        else
        {
            if (!clientId.HasValue) return Json(Array.Empty<object>());
            query = query.Where(c => c.ClientId == clientId.Value && c.IsActive);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(c => c.FullName.Contains(search.Trim()));
        }
        var items = await query.OrderByDescending(c => c.IsPrimary).ThenBy(c => c.FullName).Take(Take(take))
            .Select(c => new { id = c.ContactId, name = c.FullName, extra = c.JobTitle })
            .ToListAsync(ct);
        return Json(items);
    }

    /// <summary>Active users. Sales employees only receive themselves.</summary>
    [HttpGet]
    public async Task<IActionResult> Employees(string? search, string? id, int take = 100, CancellationToken ct = default)
    {
        var arabic = IsArabic;
        var query = Db.Users.AsNoTracking().AsQueryable();
        if (!CurrentUser.CanSeeAllData) query = query.Where(u => u.Id == CurrentUser.UserId);
        if (!string.IsNullOrEmpty(id)) query = query.Where(u => u.Id == id);
        else
        {
            query = query.Where(u => u.IsActive);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(u => u.FullName.Contains(search.Trim()) || (u.FullNameAr != null && u.FullNameAr.Contains(search.Trim())));
        }
        var items = await query.OrderBy(u => u.FullName).Take(Take(take))
            .Select(u => new { id = u.Id, name = arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName, extra = u.JobTitle })
            .ToListAsync(ct);
        return Json(items);
    }

    /// <summary>Sales managers (for the user form).</summary>
    [HttpGet]
    public async Task<IActionResult> Managers(string? search, string? id, CancellationToken ct = default)
    {
        var arabic = IsArabic;
        var managerIds = Db.UserRoles.Where(r => r.RoleId == AppRoles.SalesManagerRoleId || r.RoleId == AppRoles.AdminRoleId).Select(r => r.UserId);
        var query = Db.Users.AsNoTracking().Where(u => managerIds.Contains(u.Id));
        if (!string.IsNullOrEmpty(id)) query = query.Where(u => u.Id == id);
        else query = query.Where(u => u.IsActive);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(u => u.FullName.Contains(search.Trim()));
        var items = await query.OrderBy(u => u.FullName).Take(100)
            .Select(u => new { id = u.Id, name = arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName })
            .ToListAsync(ct);
        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> Projects(int? clientId, string? search, int? id, int take = 50, CancellationToken ct = default)
    {
        var visible = (await VisibleClientsAsync()).Select(c => c.ClientId);
        var query = Db.Projects.AsNoTracking().Where(p => visible.Contains(p.ClientId));
        if (id.HasValue) query = query.Where(p => p.ProjectId == id.Value);
        else
        {
            if (clientId.HasValue) query = query.Where(p => p.ClientId == clientId.Value);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.ProjectName.Contains(search.Trim()));
        }
        var items = await query.OrderByDescending(p => p.CreatedAt).Take(Take(take))
            .Select(p => new { id = p.ProjectId, name = p.ProjectName, extra = p.Client.CompanyName })
            .ToListAsync(ct);
        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> Tenders(int? clientId, string? search, int? id, int take = 50, CancellationToken ct = default)
    {
        var visible = (await VisibleClientsAsync()).Select(c => c.ClientId);
        var query = Db.Tenders.AsNoTracking().Where(t => visible.Contains(t.ClientId));
        if (id.HasValue) query = query.Where(t => t.TenderId == id.Value);
        else
        {
            if (clientId.HasValue) query = query.Where(t => t.ClientId == clientId.Value);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(t => t.TenderNumber.Contains(search.Trim()) || t.Title.Contains(search.Trim()));
        }
        var items = await query.OrderByDescending(t => t.CreatedAt).Take(Take(take))
            .Select(t => new { id = t.TenderId, code = t.TenderNumber, name = t.Title })
            .ToListAsync(ct);
        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> Quotations(int? clientId, string? search, int? id, int take = 50, CancellationToken ct = default)
    {
        var visible = (await VisibleClientsAsync()).Select(c => c.ClientId);
        var query = Db.Quotations.AsNoTracking().Where(q => visible.Contains(q.ClientId));
        if (id.HasValue) query = query.Where(q => q.QuotationId == id.Value);
        else
        {
            if (clientId.HasValue) query = query.Where(q => q.ClientId == clientId.Value);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(q => q.QuotationNumber.Contains(search.Trim()) || (q.Title != null && q.Title.Contains(search.Trim())));
        }
        var items = await query.OrderByDescending(q => q.CreatedAt).Take(Take(take))
            .Select(q => new { id = q.QuotationId, name = q.QuotationNumber, extra = q.Title })
            .ToListAsync(ct);
        return Json(items);
    }

    /// <summary>Distinct industries already used (for the client form suggestions).</summary>
    [HttpGet]
    public async Task<IActionResult> Industries(CancellationToken ct)
    {
        var items = await Db.Clients.AsNoTracking().Where(c => c.Industry != null && c.Industry != "")
            .Select(c => c.Industry!).Distinct().OrderBy(x => x).Take(200).ToListAsync(ct);
        return Json(items);
    }
}
