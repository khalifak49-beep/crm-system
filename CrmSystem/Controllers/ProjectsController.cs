using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;
using CrmSystem.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>Projects / business opportunities linked to a company, with notes.</summary>
public class ProjectsController : AppController
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GridRequest grid, int? clientId, ProjectStatus? status, string? userId, bool openOnly = false, CancellationToken ct = default)
    {
        var arabic = IsArabic;
        var visible = await VisibleClientsAsync();
        var uid = UserId;
        var seeAll = CurrentUser.CanSeeAllData;

        var query = Db.Projects.AsNoTracking().Where(p => visible.Any(v => v.ClientId == p.ClientId));
        if (clientId.HasValue) query = query.Where(p => p.ClientId == clientId.Value);
        if (status.HasValue) query = query.Where(p => p.ProjectStatus == status.Value);
        if (openOnly) query = query.Where(p => p.ProjectStatus != ProjectStatus.Won && p.ProjectStatus != ProjectStatus.Lost);
        if (!string.IsNullOrEmpty(userId)) query = query.Where(p => p.AssignedToId == userId);
        if (grid.SearchTerm is { } q)
            query = query.Where(p => p.ProjectName.Contains(q) || (p.ProjectDescription != null && p.ProjectDescription.Contains(q))
                                  || p.Client.CompanyName.Contains(q) || (p.Client.CompanyNameAr != null && p.Client.CompanyNameAr.Contains(q)));

        var rows = query.Select(p => new ProjectRow
        {
            ProjectId = p.ProjectId,
            ClientId = p.ClientId,
            CompanyName = arabic && p.Client.CompanyNameAr != null ? p.Client.CompanyNameAr : p.Client.CompanyName,
            ProjectName = p.ProjectName,
            ProjectStatus = (int)p.ProjectStatus,
            ProjectValue = p.ProjectValue,
            Currency = p.Currency,
            Probability = p.Probability,
            ExpectedStartDate = p.ExpectedStartDate,
            ExpectedEndDate = p.ExpectedEndDate,
            AssignedToName = p.AssignedTo == null ? null : arabic && p.AssignedTo.FullNameAr != null && p.AssignedTo.FullNameAr != "" ? p.AssignedTo.FullNameAr : p.AssignedTo.FullName,
            CreatedAt = p.CreatedAt,
            CanEdit = seeAll || p.AssignedToId == uid || p.CreatedBy == uid
        });
        return Json(await rows.ApplySort(grid, nameof(ProjectRow.CreatedAt), defaultDescending: true, tieBreaker: nameof(ProjectRow.ProjectId)).ToGridResultAsync(grid, ct));
    }

    public sealed class ProjectRow
    {
        public int ProjectId { get; set; }
        public int ClientId { get; set; }
        public string CompanyName { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public int ProjectStatus { get; set; }
        public decimal? ProjectValue { get; set; }
        public string Currency { get; set; } = "";
        public byte Probability { get; set; }
        public DateOnly? ExpectedStartDate { get; set; }
        public DateOnly? ExpectedEndDate { get; set; }
        public string? AssignedToName { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool CanEdit { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? clientId)
    {
        if (clientId.HasValue && !await CanAccessClientAsync(clientId.Value)) return NotAllowed();
        var settings = await SettingsAsync();
        var model = new ProjectFormViewModel { ClientId = clientId, AssignedToId = UserId, Currency = settings.Currency };
        await FillAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProjectFormViewModel model, CancellationToken ct)
    {
        model.ProjectId = null;
        if (model.ClientId.HasValue && !await CanAccessClientAsync(model.ClientId.Value)) return NotAllowed();
        LocalizeModelErrors();
        if (!ModelState.IsValid)
        {
            await FillAsync(model);
            return FormView("_Form", model);
        }
        var settings = await SettingsAsync();
        var project = new Project();
        model.ApplyTo(project, settings.TodayLocal);
        project.AssignedToId = CurrentUser.CanSeeAllData && !string.IsNullOrEmpty(model.AssignedToId) ? model.AssignedToId : UserId;
        Db.Projects.Add(project);
        await Db.SaveChangesAsync(ct);
        return Saved(T["Projects.Created"], new { id = project.ProjectId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var project = await LoadEditableAsync(id, ct);
        if (project == null) return NotAllowed();
        var model = ProjectFormViewModel.FromEntity(project);
        await FillAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ProjectFormViewModel model, CancellationToken ct)
    {
        var project = await LoadEditableAsync(id, ct);
        if (project == null) return NotAllowed();
        if (model.ClientId.HasValue && !await CanAccessClientAsync(model.ClientId.Value)) return NotAllowed();
        model.ProjectId = id;
        LocalizeModelErrors();
        if (!ModelState.IsValid)
        {
            await FillAsync(model);
            return FormView("_Form", model);
        }
        var settings = await SettingsAsync();
        SetOriginalRowVersion(project, model.RowVersion);
        model.ApplyTo(project, settings.TodayLocal);
        if (CurrentUser.CanSeeAllData && !string.IsNullOrEmpty(model.AssignedToId)) project.AssignedToId = model.AssignedToId;
        await Db.SaveChangesAsync(ct);
        return Saved(T["Projects.Updated"]);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var project = await LoadEditableAsync(id, ct);
        if (project == null) return NotAllowed();
        Db.Projects.Remove(project); // soft delete
        await Db.SaveChangesAsync(ct);
        return Saved(T["Projects.Deleted"], new { redirect = Request.Headers.Referer.ToString().Contains("/Projects/Details") ? Url.Action(nameof(Index)) : null });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var project = await Db.Projects.AsNoTracking().Include(p => p.Client).Include(p => p.AssignedTo).FirstOrDefaultAsync(p => p.ProjectId == id, ct);
        if (project == null) return NotFound();
        if (!await CanAccessClientAsync(project.ClientId)) return Forbid();
        var arabic = IsArabic;
        var notes = await Db.ProjectNotes.AsNoTracking().Where(n => n.ProjectId == id).OrderByDescending(n => n.CreatedAt)
            .Select(n => new { Note = n, Author = Db.Users.Where(u => u.Id == n.CreatedBy).Select(u => u.FullName).FirstOrDefault() })
            .ToListAsync(ct);

        return View(new ProjectDetailsViewModel
        {
            Project = project,
            ClientName = arabic && project.Client.CompanyNameAr != null ? project.Client.CompanyNameAr : project.Client.CompanyName,
            AssignedToName = project.AssignedTo == null ? null : project.AssignedTo.DisplayName(arabic),
            Notes = notes.Select(n => (n.Note, n.Author)).ToList(),
            CanEdit = CurrentUser.CanSeeAllData || project.AssignedToId == UserId || project.CreatedBy == UserId
        });
    }

    // ---------------------------------------------------------------- notes
    [HttpPost]
    public async Task<IActionResult> AddNote([FromBody] ProjectNoteViewModel model, CancellationToken ct)
    {
        var project = await Db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == model.ProjectId, ct);
        if (project == null || !await CanAccessClientAsync(project.ClientId)) return NotAllowed();
        if (string.IsNullOrWhiteSpace(model.NoteText)) return Fail(T["Validation.Required", T["Field.Note"]]);
        Db.ProjectNotes.Add(new ProjectNote { ProjectId = model.ProjectId, NoteText = model.NoteText.Trim() });
        await Db.SaveChangesAsync(ct);
        return Saved(T["Projects.NoteAdded"]);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteNote(int id, CancellationToken ct)
    {
        var note = await Db.ProjectNotes.Include(n => n.Project).FirstOrDefaultAsync(n => n.ProjectNoteId == id, ct);
        if (note == null) return NotFoundJson();
        if (!CurrentUser.CanSeeAllData && note.CreatedBy != UserId) return NotAllowed();
        Db.ProjectNotes.Remove(note); // soft delete
        await Db.SaveChangesAsync(ct);
        return Saved(T["Projects.NoteDeleted"]);
    }

    // ---------------------------------------------------------------- helpers
    private async Task<Project?> LoadEditableAsync(int id, CancellationToken ct)
    {
        var project = await Db.Projects.FirstOrDefaultAsync(p => p.ProjectId == id, ct);
        if (project == null || !await CanAccessClientAsync(project.ClientId)) return null;
        return CurrentUser.CanSeeAllData || project.AssignedToId == UserId || project.CreatedBy == UserId ? project : null;
    }

    /// <summary>IValidatableObject returns translation keys; translate them here.</summary>
    private void LocalizeModelErrors()
    {
        foreach (var entry in ModelState)
        {
            var errors = entry.Value.Errors.ToList();
            if (!errors.Any(e => e.ErrorMessage.StartsWith("Validation."))) continue;
            entry.Value.Errors.Clear();
            foreach (var e in errors) entry.Value.Errors.Add(e.ErrorMessage.StartsWith("Validation.") ? T[e.ErrorMessage] : e.ErrorMessage);
        }
    }

    private async Task FillAsync(ProjectFormViewModel m)
    {
        var arabic = IsArabic;
        m.CanAssign = CurrentUser.CanSeeAllData;
        if (m.ClientId.HasValue)
            m.ClientName = await Db.Clients.Where(c => c.ClientId == m.ClientId).Select(c => arabic && c.CompanyNameAr != null ? c.CompanyNameAr : c.CompanyName).FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(m.AssignedToId))
            m.AssignedToName = await Db.Users.Where(u => u.Id == m.AssignedToId).Select(u => arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName).FirstOrDefaultAsync();
    }
}
