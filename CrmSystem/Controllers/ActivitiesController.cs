using CrmSystem.Infrastructure;
using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Entities;
using CrmSystem.Models.ViewModels;
using CrmSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// Activities = every communication with a client (call, email, visit...).
/// One record per communication; several per client per day are allowed.
/// The form can also schedule the next follow-up (saved in the same transaction).
/// </summary>
public class ActivitiesController : AppController
{
    private readonly ReminderSignal _reminders;

    public ActivitiesController(ReminderSignal reminders) => _reminders = reminders;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewBag.Types = await Db.ActivityTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToListAsync();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GridRequest grid, int? clientId, int? projectId, string? typeIds, string? userId,
        DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var arabic = IsArabic;
        var visible = await VisibleClientsAsync();
        var uid = UserId;
        var seeAll = CurrentUser.CanSeeAllData;

        var query = Db.Activities.AsNoTracking().Where(a => visible.Any(v => v.ClientId == a.ClientId));
        if (clientId.HasValue) query = query.Where(a => a.ClientId == clientId.Value);
        if (projectId.HasValue) query = query.Where(a => a.ProjectId == projectId.Value);
        var types = ParseIds(typeIds);
        if (types.Count > 0) query = query.Where(a => types.Contains(a.ActivityTypeId));
        if (!string.IsNullOrEmpty(userId)) query = query.Where(a => a.PerformedById == userId);
        if (from.HasValue) query = query.Where(a => a.ActivityDate >= from.Value);
        if (to.HasValue) query = query.Where(a => a.ActivityDate <= to.Value);
        if (grid.SearchTerm is { } q)
            query = query.Where(a => (a.Notes != null && a.Notes.Contains(q)) || (a.Subject != null && a.Subject.Contains(q))
                                  || (a.Outcome != null && a.Outcome.Contains(q)) || a.Client.CompanyName.Contains(q)
                                  || (a.Client.CompanyNameAr != null && a.Client.CompanyNameAr.Contains(q))
                                  || (a.Contact != null && a.Contact.FullName.Contains(q)));

        var rows = query.Select(a => new ActivityRow
        {
            ActivityId = a.ActivityId,
            ClientId = a.ClientId,
            CompanyName = arabic && a.Client.CompanyNameAr != null ? a.Client.CompanyNameAr : a.Client.CompanyName,
            ContactName = a.Contact != null ? a.Contact.FullName : null,
            TypeName = arabic ? a.ActivityType.NameAr : a.ActivityType.NameEn,
            TypeIcon = a.ActivityType.IconName,
            TypeColor = a.ActivityType.ColorHex,
            ActivityDate = a.ActivityDate,
            ActivityTime = a.ActivityTime,
            Subject = a.Subject,
            Notes = a.Notes,
            Outcome = a.Outcome,
            PerformedByName = arabic && a.PerformedBy.FullNameAr != null && a.PerformedBy.FullNameAr != "" ? a.PerformedBy.FullNameAr : a.PerformedBy.FullName,
            CanEdit = seeAll || a.PerformedById == uid || a.CreatedBy == uid
        });
        return Json(await rows.ApplySort(grid, nameof(ActivityRow.ActivityDate), defaultDescending: true, tieBreaker: nameof(ActivityRow.ActivityId)).ToGridResultAsync(grid, ct));
    }

    public sealed class ActivityRow
    {
        public int ActivityId { get; set; }
        public int ClientId { get; set; }
        public string CompanyName { get; set; } = "";
        public string? ContactName { get; set; }
        public string TypeName { get; set; } = "";
        public string TypeIcon { get; set; } = "";
        public string TypeColor { get; set; } = "";
        public DateOnly ActivityDate { get; set; }
        public TimeOnly? ActivityTime { get; set; }
        public string? Subject { get; set; }
        public string? Notes { get; set; }
        public string? Outcome { get; set; }
        public string PerformedByName { get; set; } = "";
        public bool CanEdit { get; set; }
    }

    // ---------------------------------------------------------------- create
    [HttpGet]
    public async Task<IActionResult> Create(int? clientId, int? projectId, int? contactId)
    {
        var settings = await SettingsAsync();
        var now = settings.NowLocal;
        var model = new ActivityFormViewModel
        {
            ClientId = clientId,
            ContactId = contactId,
            ProjectId = projectId,
            ActivityDate = settings.TodayLocal,
            ActivityTime = new TimeOnly(now.Hour, now.Minute),
            PerformedById = UserId
        };
        if (clientId.HasValue && !await CanAccessClientAsync(clientId.Value)) return NotAllowed();
        FollowUpRules.SetDefaults(model, settings, UserId, User.DisplayName(IsArabic));
        await FillFormAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ActivityFormViewModel model, CancellationToken ct)
    {
        model.ActivityId = null;
        var settings = await SettingsAsync();

        // ---- quick entry: a company typed by name (ClientId = 0) ----
        Client? newClient = null;
        if (model.IsNewClient)
        {
            var name = Client.CleanName(model.NewClientName);
            if (name.Length < 2) ModelState.AddModelError(nameof(model.ClientId), T["Validation.Required", T["Field.Company"]]);
            else
            {
                // Same company already registered (case / spaces ignored)? Use it instead of creating a duplicate.
                var normalized = Client.Normalize(name);
                var existing = await Db.Clients.Where(c => c.NormalizedName == normalized).Select(c => (int?)c.ClientId).FirstOrDefaultAsync(ct);
                if (existing.HasValue)
                {
                    model.ClientId = existing;
                    if (model.ContactId == 0 && string.IsNullOrWhiteSpace(model.NewContactName)) model.ContactId = null;
                }
                else newClient = new Client { CompanyName = name, CompanyCode = null!, AccountManagerId = UserId, Source = Models.Enums.ClientSource.Manual };
            }
        }
        if (model.ClientId > 0 && !await CanAccessClientAsync(model.ClientId.Value)) return NotAllowed();

        // ---- quick entry: a person typed by name (ContactId = 0) ----
        Contact? newContact = null;
        if (model.IsNewContact)
        {
            var name = (model.NewContactName ?? "").Trim();
            if (name.Length < 2) model.ContactId = null;
            else
            {
                var existingContact = model.ClientId > 0
                    ? await Db.Contacts.Where(c => c.ClientId == model.ClientId && c.FullName == name).Select(c => (int?)c.ContactId).FirstOrDefaultAsync(ct)
                    : null;
                if (existingContact.HasValue) model.ContactId = existingContact;
                else
                {
                    var isFirst = newClient != null || !await Db.Contacts.AnyAsync(c => c.ClientId == model.ClientId, ct);
                    newContact = new Contact
                    {
                        FullName = name,
                        JobTitle = string.IsNullOrWhiteSpace(model.NewContactJobTitle) ? null : model.NewContactJobTitle.Trim(),
                        Phone = string.IsNullOrWhiteSpace(model.NewContactPhone) ? null : model.NewContactPhone.Trim(),
                        Email = string.IsNullOrWhiteSpace(model.NewContactEmail) ? null : model.NewContactEmail.Trim().ToLowerInvariant(),
                        IsPrimary = isFirst,
                        IsActive = true
                    };
                }
            }
        }

        FollowUpRules.ValidateNext(model, ModelState, settings, T);
        await ValidateLinksAsync(model, ct);
        if (!ModelState.IsValid)
        {
            await FillFormAsync(model);
            return FormView("_Form", model);
        }

        if (newClient != null) Db.Clients.Add(newClient);
        if (newContact != null)
        {
            if (newClient != null) newContact.Client = newClient; else newContact.ClientId = model.ClientId!.Value;
            Db.Contacts.Add(newContact);
        }

        var activity = new Activity();
        model.ApplyTo(activity);
        if (newClient != null) activity.Client = newClient;
        if (newContact != null) activity.Contact = newContact;
        if (newClient != null) activity.ProjectId = null; // a brand-new company has no projects yet
        activity.PerformedById = CurrentUser.CanSeeAllData && !string.IsNullOrEmpty(model.PerformedById) ? model.PerformedById : UserId;
        Db.Activities.Add(activity);

        string? warning = null;
        if (model.ScheduleFollowUp)
        {
            var followUp = FollowUpRules.Build(model, model.ClientId ?? 0, UserId, CurrentUser.CanSeeAllData, settings);
            if (newClient != null) followUp.Client = newClient;
            // follow up with the same person by default
            if (followUp.ContactId is null or 0)
            {
                followUp.ContactId = null;
                if (newContact != null) followUp.Contact = newContact;
                else followUp.ContactId = activity.ContactId;
            }
            followUp.SourceActivity = activity;
            followUp.ProjectId = activity.ProjectId;
            Db.FollowUps.Add(followUp);
            warning = FollowUpRules.LateReminderWarning(followUp, T);
        }

        await Db.SaveChangesAsync(ct); // company + person + activity + follow-up: one transaction
        if (model.ScheduleFollowUp) _reminders.Wake();

        var message = model.ScheduleFollowUp ? T["Activities.CreatedWithFollowUp"].Value : T["Activities.Created"].Value;
        if (newClient != null) message += " " + T["Activities.NewClientAdded", newClient.CompanyName].Value;
        if (newContact != null) message += " " + T["Activities.NewContactAdded", newContact.FullName].Value;
        return Saved(message, new { id = activity.ActivityId, clientId = activity.ClientId, warning });
    }

    // ---------------------------------------------------------------- edit
    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var activity = await LoadEditableAsync(id, ct);
        if (activity == null) return NotAllowed();
        var model = ActivityFormViewModel.FromEntity(activity);
        await FillFormAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ActivityFormViewModel model, CancellationToken ct)
    {
        var activity = await LoadEditableAsync(id, ct);
        if (activity == null) return NotAllowed();
        if (model.ClientId.HasValue && !await CanAccessClientAsync(model.ClientId.Value)) return NotAllowed();
        model.ActivityId = id;
        model.ScheduleFollowUp = false; // the edit form does not schedule follow-ups
        await ValidateLinksAsync(model, ct);
        if (!ModelState.IsValid)
        {
            await FillFormAsync(model);
            return FormView("_Form", model);
        }

        SetOriginalRowVersion(activity, model.RowVersion);
        model.ApplyTo(activity);
        if (CurrentUser.CanSeeAllData && !string.IsNullOrEmpty(model.PerformedById)) activity.PerformedById = model.PerformedById;
        await Db.SaveChangesAsync(ct);
        return Saved(T["Activities.Updated"]);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var activity = await LoadEditableAsync(id, ct);
        if (activity == null) return NotAllowed();
        Db.Activities.Remove(activity); // soft delete
        await Db.SaveChangesAsync(ct);
        return Saved(T["Activities.Deleted"]);
    }

    // ---------------------------------------------------------------- helpers
    private async Task<Activity?> LoadEditableAsync(int id, CancellationToken ct)
    {
        var activity = await Db.Activities.FirstOrDefaultAsync(a => a.ActivityId == id, ct);
        if (activity == null || !await CanAccessClientAsync(activity.ClientId)) return null;
        var uid = UserId;
        return CurrentUser.CanSeeAllData || activity.PerformedById == uid || activity.CreatedBy == uid ? activity : null;
    }

    /// <summary>Contact / project / follow-up contact must belong to the selected client.</summary>
    private async Task ValidateLinksAsync(ActivityFormViewModel m, CancellationToken ct)
    {
        if (!m.ClientId.HasValue || m.ClientId <= 0) return; // new company: nothing to cross-check yet
        if (m.ContactId > 0 && !await Db.Contacts.AnyAsync(c => c.ContactId == m.ContactId && c.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.ContactId), T["Validation.WrongClient"]);
        if (m.ProjectId.HasValue && !await Db.Projects.AnyAsync(p => p.ProjectId == m.ProjectId && p.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.ProjectId), T["Validation.WrongClient"]);
        if (m.ScheduleFollowUp && m.FollowUpContactId > 0 && !await Db.Contacts.AnyAsync(c => c.ContactId == m.FollowUpContactId && c.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.FollowUpContactId), T["Validation.WrongClient"]);
    }

    private async Task FillFormAsync(ActivityFormViewModel m)
    {
        var arabic = IsArabic;
        m.CanChoosePerformer = CurrentUser.CanSeeAllData;
        m.Types = await Db.ActivityTypes.AsNoTracking().Where(t => t.IsActive || t.ActivityTypeId == m.ActivityTypeId).OrderBy(t => t.SortOrder).ToListAsync();
        if (m.IsNewClient) m.ClientName = m.NewClientName;
        else if (m.ClientId.HasValue)
            m.ClientName = await Db.Clients.Where(c => c.ClientId == m.ClientId).Select(c => arabic && c.CompanyNameAr != null ? c.CompanyNameAr : c.CompanyName).FirstOrDefaultAsync();
        if (m.IsNewContact) m.ContactName = m.NewContactName;
        else if (m.ContactId.HasValue)
            m.ContactName = await Db.Contacts.Where(c => c.ContactId == m.ContactId).Select(c => c.FullName).FirstOrDefaultAsync();
        if (m.FollowUpContactId.HasValue)
            m.FollowUpContactName = await Db.Contacts.Where(c => c.ContactId == m.FollowUpContactId).Select(c => c.FullName).FirstOrDefaultAsync();
        if (m.ProjectId.HasValue)
            m.ProjectName = await Db.Projects.Where(p => p.ProjectId == m.ProjectId).Select(p => p.ProjectName).FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(m.PerformedById))
            m.PerformedByName = await UserNameAsync(m.PerformedById);
        if (!string.IsNullOrEmpty(m.FollowUpAssignedToId))
            m.FollowUpAssignedToName = await UserNameAsync(m.FollowUpAssignedToId);
    }

    private Task<string?> UserNameAsync(string id)
    {
        var arabic = IsArabic;
        return Db.Users.Where(u => u.Id == id).Select(u => arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName).FirstOrDefaultAsync();
    }

    private static List<int> ParseIds(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? new List<int>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var v) ? v : 0).Where(v => v > 0).ToList();
}
