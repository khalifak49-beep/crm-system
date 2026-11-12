using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;
using CrmSystem.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// Companies: list, add, edit, archive, soft delete, profile page with timeline.
/// Duplicate policy: same CompanyCode or same normalized CompanyName (case / spaces ignored)
/// is rejected with a clear message (and also by the unique indexes in SQL Server).
/// </summary>
public class ClientsController : AppController
{
    // ---------------------------------------------------------------- list
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GridRequest grid, ClientStatus? status, string? managerId, string? industry, CancellationToken ct)
    {
        var arabic = IsArabic;
        var visibleIds = (await VisibleClientsAsync()).Select(c => c.ClientId);
        var query = Db.ClientSummaries.AsNoTracking().Where(s => visibleIds.Contains(s.ClientId));

        if (status.HasValue) query = query.Where(s => s.Status == status.Value);
        else query = query.Where(s => s.Status != ClientStatus.Archived);
        if (!string.IsNullOrEmpty(managerId) && CurrentUser.CanSeeAllData) query = query.Where(s => s.AccountManagerId == managerId);
        if (!string.IsNullOrWhiteSpace(industry)) query = query.Where(s => s.Industry == industry);
        if (grid.SearchTerm is { } q)
            query = query.Where(s => s.CompanyName.Contains(q) || (s.CompanyNameAr != null && s.CompanyNameAr.Contains(q))
                                  || s.CompanyCode.Contains(q) || (s.Industry != null && s.Industry.Contains(q))
                                  || (s.City != null && s.City.Contains(q)) || (s.PrimaryContactName != null && s.PrimaryContactName.Contains(q))
                                  || (s.Phone != null && s.Phone.Contains(q)) || (s.Email != null && s.Email.Contains(q)));

        var rows = query.Select(s => new ClientRow
        {
            ClientId = s.ClientId,
            CompanyCode = s.CompanyCode,
            CompanyName = arabic && s.CompanyNameAr != null ? s.CompanyNameAr : s.CompanyName,
            Industry = s.Industry,
            City = s.City,
            Phone = s.Phone,
            Status = (int)s.Status,
            AccountManagerName = s.AccountManagerName,
            PrimaryContactName = s.PrimaryContactName,
            PrimaryContactPhone = s.PrimaryContactPhone,
            LastActivityDate = s.LastActivityDate,
            NextFollowUpDate = s.NextFollowUpDate,
            NextFollowUpTime = s.NextFollowUpTime,
            OverdueFollowUps = s.OverdueFollowUps,
            TotalActivities = s.TotalActivities
        });

        return Json(await rows.ApplySort(grid, nameof(ClientRow.CompanyName), tieBreaker: nameof(ClientRow.ClientId)).ToGridResultAsync(grid, ct));
    }

    public sealed class ClientRow
    {
        public int ClientId { get; set; }
        public string CompanyCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string? Industry { get; set; }
        public string? City { get; set; }
        public string? Phone { get; set; }
        public int Status { get; set; }
        public string? AccountManagerName { get; set; }
        public string? PrimaryContactName { get; set; }
        public string? PrimaryContactPhone { get; set; }
        public DateOnly? LastActivityDate { get; set; }
        public DateOnly? NextFollowUpDate { get; set; }
        public TimeOnly? NextFollowUpTime { get; set; }
        public long OverdueFollowUps { get; set; }
        public long TotalActivities { get; set; }
    }

    // ---------------------------------------------------------------- create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new ClientFormViewModel { AccountManagerId = UserId };
        await FillFormAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ClientFormViewModel model, CancellationToken ct)
    {
        model.ClientId = null;
        await ValidateDuplicatesAsync(model, ct);
        if (!ModelState.IsValid)
        {
            await FillFormAsync(model);
            return FormView("_Form", model);
        }

        var canManage = CurrentUser.CanSeeAllData;
        var client = new Client { Source = ClientSource.Manual };
        model.ApplyTo(client, canManage);
        if (!canManage) client.AccountManagerId = UserId; // employees own what they create
        if (string.IsNullOrWhiteSpace(model.CompanyCode)) client.CompanyCode = null!; // SQL generates CL-00001

        Db.Clients.Add(client);
        if (canManage) SyncAssignments(client, model.AssignedUserIds);
        await Db.SaveChangesAsync(ct);

        return Saved(T["Clients.Created", client.CompanyName], new { id = client.ClientId, redirect = Url.Action(nameof(Details), new { id = client.ClientId }) });
    }

    // ---------------------------------------------------------------- edit
    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var client = await LoadEditableAsync(id, ct);
        if (client == null) return NotAllowed();
        var model = ClientFormViewModel.FromEntity(client);
        await FillFormAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ClientFormViewModel model, CancellationToken ct)
    {
        var client = await LoadEditableAsync(id, ct);
        if (client == null) return NotAllowed();
        model.ClientId = id;
        await ValidateDuplicatesAsync(model, ct);
        if (!ModelState.IsValid)
        {
            await FillFormAsync(model);
            return FormView("_Form", model);
        }

        SetOriginalRowVersion(client, model.RowVersion);
        var canManage = CurrentUser.CanSeeAllData;
        model.ApplyTo(client, canManage);
        if (canManage) SyncAssignments(client, model.AssignedUserIds);
        await Db.SaveChangesAsync(ct);
        return Saved(T["Clients.Updated", client.CompanyName]);
    }

    // ---------------------------------------------------------------- archive / restore / delete
    [HttpPost]
    public async Task<IActionResult> Archive(int id, CancellationToken ct)
    {
        var client = await LoadEditableAsync(id, ct);
        if (client == null) return NotAllowed();
        client.Status = client.Status == ClientStatus.Archived ? ClientStatus.Active : ClientStatus.Archived;
        await Db.SaveChangesAsync(ct);
        return Saved(client.Status == ClientStatus.Archived ? T["Clients.Archived"] : T["Clients.Restored"]);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!CurrentUser.CanSeeAllData) return NotAllowed();
        var client = await Db.Clients.FirstOrDefaultAsync(c => c.ClientId == id, ct);
        if (client == null) return NotFoundJson();
        Db.Clients.Remove(client); // soft delete (history is kept)
        await Db.SaveChangesAsync(ct);
        return Saved(T["Clients.Deleted"], new { redirect = Url.Action(nameof(Index)) });
    }

    // ---------------------------------------------------------------- profile
    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        if (!await CanAccessClientAsync(id)) return Forbid();
        var client = await Db.Clients.AsNoTracking().Include(c => c.Assignments).FirstOrDefaultAsync(c => c.ClientId == id, ct);
        var summary = await Db.ClientSummaries.AsNoTracking().FirstOrDefaultAsync(s => s.ClientId == id, ct);
        if (client == null || summary == null) return NotFound();

        var now = DateTime.UtcNow;
        var open = Db.FollowUps.AsNoTracking().Where(f => f.ClientId == id && (f.Status == FollowUpStatus.Pending || f.Status == FollowUpStatus.Overdue));
        var assignedIds = client.Assignments.Select(a => a.UserId).ToList();
        var arabic = IsArabic;

        var model = new ClientProfileViewModel
        {
            Client = client,
            Summary = summary,
            Contacts = await Db.Contacts.AsNoTracking().Where(c => c.ClientId == id).OrderByDescending(c => c.IsPrimary).ThenBy(c => c.FullName).ToListAsync(ct),
            AccountManagerName = summary.AccountManagerName,
            AssignedNames = await Db.Users.AsNoTracking().Where(u => assignedIds.Contains(u.Id))
                .Select(u => arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName).ToListAsync(ct),
            OpenProjects = await Db.Projects.CountAsync(p => p.ClientId == id && p.ProjectStatus != ProjectStatus.Won && p.ProjectStatus != ProjectStatus.Lost, ct),
            UpcomingFollowUps = await open.CountAsync(f => f.DueAtUtc >= now, ct),
            OverdueFollowUps = await open.CountAsync(f => f.DueAtUtc < now, ct),
            CompletedFollowUps = await Db.FollowUps.CountAsync(f => f.ClientId == id && f.Status == FollowUpStatus.Completed, ct),
            Quotations = await Db.Quotations.CountAsync(q => q.ClientId == id, ct),
            Tenders = await Db.Tenders.CountAsync(t => t.ClientId == id, ct),
            CanEdit = await CanEditAsync(client, ct),
            CanDelete = CurrentUser.CanSeeAllData
        };
        return View(model);
    }

    /// <summary>GET /Clients/Timeline/5?skip=0&amp;take=30&amp;kind=Activity : newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> Timeline(int id, int skip = 0, int take = 30, string? kind = null, CancellationToken ct = default)
    {
        if (!await CanAccessClientAsync(id)) return NotAllowed();
        var arabic = IsArabic;
        var query = Db.ClientTimeline.AsNoTracking().Where(e => e.ClientId == id);
        if (!string.IsNullOrEmpty(kind)) query = query.Where(e => e.EventKind == kind);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.EventAtLocal).ThenByDescending(e => e.SortKey)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100))
            .Select(e => new
            {
                at = e.EventAtLocal,
                kind = e.EventKind,
                code = e.EventCode,
                title = arabic ? e.TitleAr : e.TitleEn,
                details = e.Details,
                outcome = e.Outcome,
                icon = e.IconName,
                color = e.ColorHex,
                referenceId = e.ReferenceId
            })
            .ToListAsync(ct);
        return Json(new { total, items });
    }

    // ---------------------------------------------------------------- helpers
    private async Task<bool> CanEditAsync(Client client, CancellationToken ct) =>
        CurrentUser.CanSeeAllData
        || client.AccountManagerId == UserId
        || await Db.ClientAssignments.AnyAsync(a => a.ClientId == client.ClientId && a.UserId == UserId && a.CanEdit, ct);

    private async Task<Client?> LoadEditableAsync(int id, CancellationToken ct)
    {
        if (!await CanAccessClientAsync(id)) return null;
        var client = await Db.Clients.Include(c => c.Assignments).FirstOrDefaultAsync(c => c.ClientId == id, ct);
        if (client == null || !await CanEditAsync(client, ct)) return null;
        return client;
    }

    private async Task ValidateDuplicatesAsync(ClientFormViewModel model, CancellationToken ct)
    {
        var normalized = Client.Normalize(model.CompanyName);
        if (normalized.Length > 0)
        {
            var existing = await Db.Clients.AsNoTracking()
                .Where(c => c.NormalizedName == normalized && c.ClientId != (model.ClientId ?? 0))
                .Select(c => new { c.CompanyCode, c.CompanyName }).FirstOrDefaultAsync(ct);
            if (existing != null)
                ModelState.AddModelError(nameof(model.CompanyName), T["Clients.DuplicateName", existing.CompanyName, existing.CompanyCode]);
        }
        if (!string.IsNullOrWhiteSpace(model.CompanyCode))
        {
            var code = model.CompanyCode.Trim().ToUpperInvariant();
            if (await Db.Clients.AnyAsync(c => c.CompanyCode == code && c.ClientId != (model.ClientId ?? 0), ct))
                ModelState.AddModelError(nameof(model.CompanyCode), T["Clients.DuplicateCode", code]);
        }
    }

    private void SyncAssignments(Client client, List<string> userIds)
    {
        var wanted = userIds.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToHashSet();
        foreach (var a in client.Assignments.Where(a => !wanted.Contains(a.UserId)).ToList())
            client.Assignments.Remove(a);
        foreach (var userId in wanted.Where(u => client.Assignments.All(a => a.UserId != u)))
            client.Assignments.Add(new ClientAssignment { UserId = userId, AssignedBy = UserId, AssignedAtUtc = DateTime.UtcNow });
    }

    private async Task FillFormAsync(ClientFormViewModel model)
    {
        model.CanManageOwnership = CurrentUser.CanSeeAllData;
        var arabic = IsArabic;
        if (model.CanManageOwnership)
        {
            model.UserOptions = (await Db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.FullName)
                    .Select(u => new { u.Id, Name = arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName }).ToListAsync())
                .Select(u => (u.Id, u.Name)).ToList();
        }
        model.Industries = await Db.Clients.AsNoTracking().Where(c => c.Industry != null && c.Industry != "")
            .Select(c => c.Industry!).Distinct().OrderBy(x => x).Take(200).ToListAsync();
    }
}
