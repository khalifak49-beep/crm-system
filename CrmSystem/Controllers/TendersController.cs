using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;
using CrmSystem.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// Tenders issued by clients. Tender numbers are unique per client.
/// Closing reminders are created by the ReminderEngine (Reminders.TenderClosingDaysBefore).
/// The page itself is Quotations/Index (tab "Tenders").
/// </summary>
public class TendersController : AppController
{
    [HttpGet]
    public IActionResult Index() => RedirectToAction("Index", "Quotations", new { tab = "tenders" });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GridRequest grid, int? clientId, int? projectId, TenderStatus? status, CancellationToken ct = default)
    {
        var arabic = IsArabic;
        var visible = await VisibleClientsAsync();
        var uid = UserId;
        var seeAll = CurrentUser.CanSeeAllData;

        var query = Db.Tenders.AsNoTracking().Where(t => visible.Any(v => v.ClientId == t.ClientId));
        if (clientId.HasValue) query = query.Where(t => t.ClientId == clientId.Value);
        if (projectId.HasValue) query = query.Where(t => t.ProjectId == projectId.Value);
        if (status.HasValue) query = query.Where(t => t.Status == status.Value);
        if (grid.SearchTerm is { } s)
            query = query.Where(t => t.TenderNumber.Contains(s) || t.Title.Contains(s) || t.Client.CompanyName.Contains(s)
                                  || (t.Client.CompanyNameAr != null && t.Client.CompanyNameAr.Contains(s)));

        var rows = query.Select(t => new TenderRow
        {
            TenderId = t.TenderId,
            TenderNumber = t.TenderNumber,
            ClientId = t.ClientId,
            CompanyName = arabic && t.Client.CompanyNameAr != null ? t.Client.CompanyNameAr : t.Client.CompanyName,
            Title = t.Title,
            SubmissionDate = t.SubmissionDate,
            ClosingDate = t.ClosingDate,
            TenderValue = t.TenderValue,
            Currency = t.Currency,
            Status = (int)t.Status,
            CanEdit = seeAll || t.AssignedToId == uid || t.CreatedBy == uid
        });
        return Json(await rows.ApplySort(grid, nameof(TenderRow.TenderId), defaultDescending: true, tieBreaker: nameof(TenderRow.TenderId)).ToGridResultAsync(grid, ct));
    }

    public sealed class TenderRow
    {
        public int TenderId { get; set; }
        public string TenderNumber { get; set; } = "";
        public int ClientId { get; set; }
        public string CompanyName { get; set; } = "";
        public string Title { get; set; } = "";
        public DateOnly? SubmissionDate { get; set; }
        public DateTime? ClosingDate { get; set; }
        public decimal? TenderValue { get; set; }
        public string Currency { get; set; } = "";
        public int Status { get; set; }
        public bool CanEdit { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? clientId, int? projectId)
    {
        if (clientId.HasValue && !await CanAccessClientAsync(clientId.Value)) return NotAllowed();
        var settings = await SettingsAsync();
        var model = new TenderFormViewModel { ClientId = clientId, ProjectId = projectId, IssueDate = settings.TodayLocal, Currency = settings.Currency, AssignedToId = UserId };
        await FillAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(TenderFormViewModel model, CancellationToken ct)
    {
        model.TenderId = null;
        if (model.ClientId.HasValue && !await CanAccessClientAsync(model.ClientId.Value)) return NotAllowed();
        await ValidateAsync(model, ct);
        if (!ModelState.IsValid)
        {
            await FillAsync(model);
            return FormView("_Form", model);
        }
        var tender = new Tender();
        model.ApplyTo(tender);
        tender.AssignedToId = CurrentUser.CanSeeAllData && !string.IsNullOrEmpty(model.AssignedToId) ? model.AssignedToId : UserId;
        Db.Tenders.Add(tender);
        await Db.SaveChangesAsync(ct);
        return Saved(T["Tenders.Created", tender.TenderNumber]);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var tender = await LoadEditableAsync(id, ct);
        if (tender == null) return NotAllowed();
        var model = TenderFormViewModel.FromEntity(tender);
        await FillAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, TenderFormViewModel model, CancellationToken ct)
    {
        var tender = await LoadEditableAsync(id, ct);
        if (tender == null) return NotAllowed();
        if (model.ClientId.HasValue && !await CanAccessClientAsync(model.ClientId.Value)) return NotAllowed();
        model.TenderId = id;
        await ValidateAsync(model, ct);
        if (!ModelState.IsValid)
        {
            await FillAsync(model);
            return FormView("_Form", model);
        }
        SetOriginalRowVersion(tender, model.RowVersion);
        model.ApplyTo(tender);
        if (CurrentUser.CanSeeAllData && !string.IsNullOrEmpty(model.AssignedToId)) tender.AssignedToId = model.AssignedToId;
        await Db.SaveChangesAsync(ct);
        return Saved(T["Tenders.Updated", tender.TenderNumber]);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var tender = await LoadEditableAsync(id, ct);
        if (tender == null) return NotAllowed();
        Db.Tenders.Remove(tender); // soft delete
        await Db.SaveChangesAsync(ct);
        return Saved(T["Tenders.Deleted"]);
    }

    private async Task ValidateAsync(TenderFormViewModel m, CancellationToken ct)
    {
        if (!m.ClientId.HasValue) return;
        if (!string.IsNullOrWhiteSpace(m.TenderNumber))
        {
            var number = m.TenderNumber.Trim();
            if (await Db.Tenders.AnyAsync(t => t.ClientId == m.ClientId && t.TenderNumber == number && t.TenderId != (m.TenderId ?? 0), ct))
                ModelState.AddModelError(nameof(m.TenderNumber), T["Tenders.DuplicateNumber", number]);
        }
        if (m.ProjectId.HasValue && !await Db.Projects.AnyAsync(p => p.ProjectId == m.ProjectId && p.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.ProjectId), T["Validation.WrongClient"]);
    }

    private async Task<Tender?> LoadEditableAsync(int id, CancellationToken ct)
    {
        var tender = await Db.Tenders.FirstOrDefaultAsync(t => t.TenderId == id, ct);
        if (tender == null || !await CanAccessClientAsync(tender.ClientId)) return null;
        return CurrentUser.CanSeeAllData || tender.AssignedToId == UserId || tender.CreatedBy == UserId ? tender : null;
    }

    private async Task FillAsync(TenderFormViewModel m)
    {
        var arabic = IsArabic;
        m.CanAssign = CurrentUser.CanSeeAllData;
        if (m.ClientId.HasValue) m.ClientName = await Db.Clients.Where(c => c.ClientId == m.ClientId).Select(c => arabic && c.CompanyNameAr != null ? c.CompanyNameAr : c.CompanyName).FirstOrDefaultAsync();
        if (m.ProjectId.HasValue) m.ProjectName = await Db.Projects.Where(p => p.ProjectId == m.ProjectId).Select(p => p.ProjectName).FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(m.AssignedToId)) m.AssignedToName = await Db.Users.Where(u => u.Id == m.AssignedToId).Select(u => arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName).FirstOrDefaultAsync();
    }
}
