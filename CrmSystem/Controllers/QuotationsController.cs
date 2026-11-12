using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;
using CrmSystem.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// Quotations (and the "Quotations &amp; Tenders" page). Quotation numbers are unique.
/// Expiry reminders are created by the ReminderEngine (Reminders.QuotationExpiryDaysBefore).
/// </summary>
public class QuotationsController : AppController
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GridRequest grid, int? clientId, int? projectId, int? tenderId, QuotationStatus? status, bool expiringOnly = false, CancellationToken ct = default)
    {
        var arabic = IsArabic;
        var visible = await VisibleClientsAsync();
        var uid = UserId;
        var seeAll = CurrentUser.CanSeeAllData;
        var settings = await SettingsAsync();

        var query = Db.Quotations.AsNoTracking().Where(q => visible.Any(v => v.ClientId == q.ClientId));
        if (clientId.HasValue) query = query.Where(q => q.ClientId == clientId.Value);
        if (projectId.HasValue) query = query.Where(q => q.ProjectId == projectId.Value);
        if (tenderId.HasValue) query = query.Where(q => q.TenderId == tenderId.Value);
        if (status.HasValue) query = query.Where(q => q.Status == status.Value);
        if (expiringOnly)
        {
            var limit = settings.TodayLocal.AddDays(14);
            query = query.Where(q => (q.Status == QuotationStatus.Sent || q.Status == QuotationStatus.UnderReview) && q.ValidUntil != null && q.ValidUntil <= limit);
        }
        if (grid.SearchTerm is { } s)
            query = query.Where(q => q.QuotationNumber.Contains(s) || (q.Title != null && q.Title.Contains(s)) || q.Client.CompanyName.Contains(s)
                                  || (q.Client.CompanyNameAr != null && q.Client.CompanyNameAr.Contains(s)) || (q.Notes != null && q.Notes.Contains(s)));

        var rows = query.Select(q => new QuotationRow
        {
            QuotationId = q.QuotationId,
            QuotationNumber = q.QuotationNumber,
            ClientId = q.ClientId,
            CompanyName = arabic && q.Client.CompanyNameAr != null ? q.Client.CompanyNameAr : q.Client.CompanyName,
            Title = q.Title,
            SubmissionDate = q.SubmissionDate,
            QuotationValue = q.QuotationValue,
            Currency = q.Currency,
            ValidUntil = q.ValidUntil,
            Status = (int)q.Status,
            TenderNumber = q.Tender != null ? q.Tender.TenderNumber : null,
            CanEdit = seeAll || q.AssignedToId == uid || q.CreatedBy == uid
        });
        return Json(await rows.ApplySort(grid, nameof(QuotationRow.QuotationId), defaultDescending: true, tieBreaker: nameof(QuotationRow.QuotationId)).ToGridResultAsync(grid, ct));
    }

    public sealed class QuotationRow
    {
        public int QuotationId { get; set; }
        public string QuotationNumber { get; set; } = "";
        public int ClientId { get; set; }
        public string CompanyName { get; set; } = "";
        public string? Title { get; set; }
        public DateOnly? SubmissionDate { get; set; }
        public decimal? QuotationValue { get; set; }
        public string Currency { get; set; } = "";
        public DateOnly? ValidUntil { get; set; }
        public int Status { get; set; }
        public string? TenderNumber { get; set; }
        public bool CanEdit { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? clientId, int? projectId, int? tenderId, CancellationToken ct)
    {
        if (clientId.HasValue && !await CanAccessClientAsync(clientId.Value)) return NotAllowed();
        var settings = await SettingsAsync();
        var model = new QuotationFormViewModel
        {
            ClientId = clientId,
            ProjectId = projectId,
            TenderId = tenderId,
            QuotationNumber = await NextNumberAsync(settings.TodayLocal.Year, ct),
            SubmissionDate = settings.TodayLocal,
            ValidUntil = settings.TodayLocal.AddDays(30),
            Currency = settings.Currency,
            AssignedToId = UserId
        };
        await FillAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(QuotationFormViewModel model, CancellationToken ct)
    {
        model.QuotationId = null;
        if (model.ClientId.HasValue && !await CanAccessClientAsync(model.ClientId.Value)) return NotAllowed();
        await ValidateAsync(model, ct);
        if (!ModelState.IsValid)
        {
            await FillAsync(model);
            return FormView("_Form", model);
        }
        var quotation = new Quotation();
        model.ApplyTo(quotation);
        quotation.AssignedToId = CurrentUser.CanSeeAllData && !string.IsNullOrEmpty(model.AssignedToId) ? model.AssignedToId : UserId;
        Db.Quotations.Add(quotation);
        await Db.SaveChangesAsync(ct);
        return Saved(T["Quotations.Created", quotation.QuotationNumber]);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var quotation = await LoadEditableAsync(id, ct);
        if (quotation == null) return NotAllowed();
        var model = QuotationFormViewModel.FromEntity(quotation);
        await FillAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, QuotationFormViewModel model, CancellationToken ct)
    {
        var quotation = await LoadEditableAsync(id, ct);
        if (quotation == null) return NotAllowed();
        if (model.ClientId.HasValue && !await CanAccessClientAsync(model.ClientId.Value)) return NotAllowed();
        model.QuotationId = id;
        await ValidateAsync(model, ct);
        if (!ModelState.IsValid)
        {
            await FillAsync(model);
            return FormView("_Form", model);
        }
        SetOriginalRowVersion(quotation, model.RowVersion);
        model.ApplyTo(quotation);
        if (CurrentUser.CanSeeAllData && !string.IsNullOrEmpty(model.AssignedToId)) quotation.AssignedToId = model.AssignedToId;
        await Db.SaveChangesAsync(ct);
        return Saved(T["Quotations.Updated", quotation.QuotationNumber]);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var quotation = await LoadEditableAsync(id, ct);
        if (quotation == null) return NotAllowed();
        Db.Quotations.Remove(quotation); // soft delete
        await Db.SaveChangesAsync(ct);
        return Saved(T["Quotations.Deleted"]);
    }

    // ---------------------------------------------------------------- helpers
    private async Task<string> NextNumberAsync(int year, CancellationToken ct)
    {
        var prefix = $"Q-{year}-";
        var count = await Db.Quotations.IgnoreQueryFilters().CountAsync(q => q.QuotationNumber.StartsWith(prefix), ct);
        for (var n = count + 1; ; n++)
        {
            var candidate = $"{prefix}{n:0000}";
            if (!await Db.Quotations.AnyAsync(q => q.QuotationNumber == candidate, ct)) return candidate;
        }
    }

    private async Task ValidateAsync(QuotationFormViewModel m, CancellationToken ct)
    {
        if (m.SubmissionDate.HasValue && m.ValidUntil.HasValue && m.ValidUntil < m.SubmissionDate)
            ModelState.AddModelError(nameof(m.ValidUntil), T["Validation.EndBeforeStart"]);
        if (!string.IsNullOrWhiteSpace(m.QuotationNumber))
        {
            var number = m.QuotationNumber.Trim();
            if (await Db.Quotations.AnyAsync(q => q.QuotationNumber == number && q.QuotationId != (m.QuotationId ?? 0), ct))
                ModelState.AddModelError(nameof(m.QuotationNumber), T["Quotations.DuplicateNumber", number]);
        }
        if (!m.ClientId.HasValue) return;
        if (m.ContactId.HasValue && !await Db.Contacts.AnyAsync(c => c.ContactId == m.ContactId && c.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.ContactId), T["Validation.WrongClient"]);
        if (m.ProjectId.HasValue && !await Db.Projects.AnyAsync(p => p.ProjectId == m.ProjectId && p.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.ProjectId), T["Validation.WrongClient"]);
        if (m.TenderId.HasValue && !await Db.Tenders.AnyAsync(t => t.TenderId == m.TenderId && t.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.TenderId), T["Validation.WrongClient"]);
    }

    private async Task<Quotation?> LoadEditableAsync(int id, CancellationToken ct)
    {
        var quotation = await Db.Quotations.FirstOrDefaultAsync(q => q.QuotationId == id, ct);
        if (quotation == null || !await CanAccessClientAsync(quotation.ClientId)) return null;
        return CurrentUser.CanSeeAllData || quotation.AssignedToId == UserId || quotation.CreatedBy == UserId ? quotation : null;
    }

    private async Task FillAsync(QuotationFormViewModel m)
    {
        var arabic = IsArabic;
        m.CanAssign = CurrentUser.CanSeeAllData;
        if (m.ClientId.HasValue) m.ClientName = await Db.Clients.Where(c => c.ClientId == m.ClientId).Select(c => arabic && c.CompanyNameAr != null ? c.CompanyNameAr : c.CompanyName).FirstOrDefaultAsync();
        if (m.ContactId.HasValue) m.ContactName = await Db.Contacts.Where(c => c.ContactId == m.ContactId).Select(c => c.FullName).FirstOrDefaultAsync();
        if (m.ProjectId.HasValue) m.ProjectName = await Db.Projects.Where(p => p.ProjectId == m.ProjectId).Select(p => p.ProjectName).FirstOrDefaultAsync();
        if (m.TenderId.HasValue) m.TenderName = await Db.Tenders.Where(t => t.TenderId == m.TenderId).Select(t => t.TenderNumber + " - " + t.Title).FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(m.AssignedToId)) m.AssignedToName = await Db.Users.Where(u => u.Id == m.AssignedToId).Select(u => arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName).FirstOrDefaultAsync();
    }
}
