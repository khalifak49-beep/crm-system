using CrmSystem.Infrastructure;
using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;
using CrmSystem.Models.ReadModels;
using CrmSystem.Models.ViewModels;
using CrmSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// Follow-ups: scheduled next contacts.
/// Tabs: today, tomorrow, week (next 7 days), upcoming, overdue, completed, cancelled, all.
/// Day boundaries use the system time zone (Asia/Muscat).
/// Sales employees work on follow-ups assigned to them (or created by them).
/// </summary>
public class FollowUpsController : AppController
{
    private readonly ReminderSignal _reminders;

    public FollowUpsController(ReminderSignal reminders) => _reminders = reminders;

    public static readonly string[] Tabs = { "today", "tomorrow", "week", "upcoming", "overdue", "completed", "cancelled", "all" };

    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>Base query: follow-ups the user may see.</summary>
    private async Task<IQueryable<FollowUpDetails>> ScopedAsync(int? clientId, int? projectId, string? userId)
    {
        var visible = await VisibleClientsAsync();
        var query = Db.FollowUpDetails.AsNoTracking().Where(f => visible.Any(v => v.ClientId == f.ClientId));
        if (!CurrentUser.CanSeeAllData)
        {
            var uid = UserId;
            query = query.Where(f => f.AssignedToId == uid || f.CreatedBy == uid);
        }
        else if (!string.IsNullOrEmpty(userId)) query = query.Where(f => f.AssignedToId == userId);
        if (clientId.HasValue) query = query.Where(f => f.ClientId == clientId.Value);
        if (projectId.HasValue) query = query.Where(f => f.ProjectId == projectId.Value);
        return query;
    }

    private static IQueryable<FollowUpDetails> ApplyTab(IQueryable<FollowUpDetails> q, string? tab, SettingsSnapshot s)
    {
        var now = DateTime.UtcNow;
        var today = s.TodayLocal;
        var (todayStart, tomorrowStart) = s.LocalDayRangeUtc(today);
        var (_, dayAfterStart) = s.LocalDayRangeUtc(today.AddDays(1));
        var weekEnd = s.LocalDayRangeUtc(today.AddDays(6)).EndUtc;
        var open = q.Where(f => f.Status == FollowUpStatus.Pending || f.Status == FollowUpStatus.Overdue);

        return tab switch
        {
            "today" => open.Where(f => f.DueAtUtc >= todayStart && f.DueAtUtc < tomorrowStart),
            "tomorrow" => open.Where(f => f.DueAtUtc >= tomorrowStart && f.DueAtUtc < dayAfterStart),
            "week" => open.Where(f => f.DueAtUtc >= todayStart && f.DueAtUtc < weekEnd),
            "upcoming" => open.Where(f => f.DueAtUtc >= now),
            "overdue" => open.Where(f => f.DueAtUtc < now),
            "completed" => q.Where(f => f.Status == FollowUpStatus.Completed),
            "cancelled" => q.Where(f => f.Status == FollowUpStatus.Cancelled),
            "open" => open,
            _ => q
        };
    }

    [HttpGet]
    public async Task<IActionResult> Counts(int? clientId, string? userId, CancellationToken ct)
    {
        var settings = await SettingsAsync();
        var scoped = await ScopedAsync(clientId, null, userId);
        var result = new Dictionary<string, int>();
        foreach (var tab in Tabs)
            result[tab] = await ApplyTab(scoped, tab, settings).CountAsync(ct);
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GridRequest grid, string? tab, int? clientId, int? projectId, string? userId, FollowUpPriority? priority, CancellationToken ct)
    {
        var settings = await SettingsAsync();
        var arabic = IsArabic;
        var uid = UserId;
        var seeAll = CurrentUser.CanSeeAllData;

        var query = ApplyTab(await ScopedAsync(clientId, projectId, userId), tab ?? "open", settings);
        if (priority.HasValue) query = query.Where(f => f.Priority == priority.Value);
        if (grid.SearchTerm is { } q)
            query = query.Where(f => f.Description.Contains(q) || f.CompanyName.Contains(q) || (f.CompanyNameAr != null && f.CompanyNameAr.Contains(q))
                                  || (f.ContactName != null && f.ContactName.Contains(q)) || f.AssignedToName.Contains(q));

        var rows = query.Select(f => new FollowUpRow
        {
            FollowUpId = f.FollowUpId,
            ClientId = f.ClientId,
            CompanyName = arabic && f.CompanyNameAr != null ? f.CompanyNameAr : f.CompanyName,
            ContactName = f.ContactName,
            ContactPhone = f.ContactMobile ?? f.ContactPhone,
            TypeName = arabic ? f.FollowUpTypeNameAr : f.FollowUpTypeNameEn,
            TypeIcon = f.FollowUpTypeIcon,
            TypeColor = f.FollowUpTypeColor,
            Description = f.Description,
            DueDate = f.DueDate,
            DueTime = f.DueTime,
            DueAtUtc = f.DueAtUtc,
            Priority = (int)f.Priority,
            EffectiveStatus = (int)f.EffectiveStatus,
            AssignedToName = f.AssignedToName,
            IsOpen = f.Status == FollowUpStatus.Pending || f.Status == FollowUpStatus.Overdue,
            CanEdit = seeAll || f.AssignedToId == uid || f.CreatedBy == uid
        });

        var completedTab = tab is "completed" or "cancelled";
        return Json(await rows.ApplySort(grid, nameof(FollowUpRow.DueAtUtc), defaultDescending: completedTab, tieBreaker: nameof(FollowUpRow.FollowUpId)).ToGridResultAsync(grid, ct));
    }

    public sealed class FollowUpRow
    {
        public int FollowUpId { get; set; }
        public int ClientId { get; set; }
        public string CompanyName { get; set; } = "";
        public string? ContactName { get; set; }
        public string? ContactPhone { get; set; }
        public string TypeName { get; set; } = "";
        public string TypeIcon { get; set; } = "";
        public string TypeColor { get; set; } = "";
        public string Description { get; set; } = "";
        public DateOnly DueDate { get; set; }
        public TimeOnly DueTime { get; set; }
        public DateTime DueAtUtc { get; set; }
        public int Priority { get; set; }
        public int EffectiveStatus { get; set; }
        public string AssignedToName { get; set; } = "";
        public bool IsOpen { get; set; }
        public bool CanEdit { get; set; }
    }

    // ---------------------------------------------------------------- details
    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var item = await (await ScopedAsync(null, null, null)).FirstOrDefaultAsync(f => f.FollowUpId == id, ct);
        if (item == null) return NotAllowed();
        var client = await Db.Clients.AsNoTracking().FirstAsync(c => c.ClientId == item.ClientId, ct);

        // follow-up chain: parents and children
        var chain = new List<(int, DateOnly, string, FollowUpStatus)>();
        var all = await Db.FollowUps.AsNoTracking().Where(f => f.ClientId == item.ClientId && (f.FollowUpId == item.ParentFollowUpId || f.ParentFollowUpId == id))
            .Select(f => new { f.FollowUpId, f.DueDate, f.Description, f.Status }).ToListAsync(ct);
        chain.AddRange(all.Select(f => (f.FollowUpId, f.DueDate, f.Description, f.Status)));

        var model = new FollowUpDetailsViewModel
        {
            Item = item,
            Client = client,
            PrimaryContact = await Db.Contacts.Where(c => c.ClientId == item.ClientId && c.IsPrimary).Select(c => c.FullName).FirstOrDefaultAsync(ct),
            RecentActivities = await Db.ActivityDetails.AsNoTracking().Where(a => a.ClientId == item.ClientId)
                .OrderByDescending(a => a.ActivityDate).ThenByDescending(a => a.ActivityId).Take(5).ToListAsync(ct),
            Chain = chain,
            CanEdit = CurrentUser.CanSeeAllData || item.AssignedToId == UserId || item.CreatedBy == UserId
        };
        return PartialView("_Details", model);
    }

    // ---------------------------------------------------------------- create / edit
    [HttpGet]
    public async Task<IActionResult> Create(int? clientId, int? projectId, int? quotationId, int? tenderId, int? sourceActivityId)
    {
        if (clientId.HasValue && !await CanAccessClientAsync(clientId.Value)) return NotAllowed();
        var settings = await SettingsAsync();
        var model = new FollowUpFormViewModel
        {
            ClientId = clientId,
            ProjectId = projectId,
            QuotationId = quotationId,
            TenderId = tenderId,
            SourceActivityId = sourceActivityId,
            DueDate = settings.TodayLocal.AddDays(1),
            DueTime = new TimeOnly(9, 0),
            AssignedToId = UserId,
            ReminderMinutesBefore = settings.FollowUpReminderMinutes
        };
        await FillFormAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(FollowUpFormViewModel model, CancellationToken ct)
    {
        model.FollowUpId = null;
        var settings = await SettingsAsync();
        if (model.ClientId.HasValue && !await CanAccessClientAsync(model.ClientId.Value)) return NotAllowed();
        await ValidateAsync(model, settings, ct);
        if (!ModelState.IsValid)
        {
            await FillFormAsync(model);
            return FormView("_Form", model);
        }

        var followUp = new FollowUp { Status = FollowUpStatus.Pending, ReminderStatus = ReminderStatus.Scheduled };
        Apply(model, followUp, settings);
        Db.FollowUps.Add(followUp);
        await Db.SaveChangesAsync(ct);
        _reminders.Wake();
        return Saved(T["FollowUps.Created"], new { id = followUp.FollowUpId, warning = FollowUpRules.LateReminderWarning(followUp, T) });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var followUp = await LoadEditableAsync(id, ct);
        if (followUp == null) return NotAllowed();
        if (!followUp.IsOpen) return Fail(T["FollowUps.NotOpen"]);
        var model = FollowUpFormViewModel.FromEntity(followUp);
        await FillFormAsync(model);
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, FollowUpFormViewModel model, CancellationToken ct)
    {
        var followUp = await LoadEditableAsync(id, ct);
        if (followUp == null) return NotAllowed();
        if (!followUp.IsOpen) return Fail(T["FollowUps.NotOpen"]);
        if (model.ClientId.HasValue && !await CanAccessClientAsync(model.ClientId.Value)) return NotAllowed();
        model.FollowUpId = id;
        var settings = await SettingsAsync();
        await ValidateAsync(model, settings, ct);
        if (!ModelState.IsValid)
        {
            await FillFormAsync(model);
            return FormView("_Form", model);
        }

        SetOriginalRowVersion(followUp, model.RowVersion);
        var oldReminderMinutes = followUp.ReminderMinutesBefore;
        Apply(model, followUp, settings);
        if (oldReminderMinutes != followUp.ReminderMinutesBefore && followUp.ReminderStatus != ReminderStatus.Scheduled)
        {
            followUp.ReminderStatus = ReminderStatus.Scheduled; // new reminder offset -> remind again
            followUp.ReminderSentAtUtc = null;
        }
        if (followUp.Status == FollowUpStatus.Overdue && followUp.DueAtUtc > DateTime.UtcNow)
            followUp.Status = FollowUpStatus.Pending; // rescheduled into the future

        await Db.SaveChangesAsync(ct);
        _reminders.Wake();
        return Saved(T["FollowUps.Updated"], new { warning = followUp.ReminderStatus == ReminderStatus.Scheduled ? FollowUpRules.LateReminderWarning(followUp, T) : null });
    }

    // ---------------------------------------------------------------- complete
    [HttpGet]
    public async Task<IActionResult> Complete(int id, CancellationToken ct)
    {
        var followUp = await LoadEditableAsync(id, ct);
        if (followUp == null) return NotAllowed();
        if (!followUp.IsOpen) return Fail(T["FollowUps.NotOpen"]);
        var settings = await SettingsAsync();
        var now = settings.NowLocal;
        var model = new CompleteFollowUpViewModel
        {
            FollowUpId = id,
            ClientId = followUp.ClientId,
            ActivityTypeId = followUp.FollowUpTypeId,
            ActivityDate = settings.TodayLocal,
            ActivityTime = new TimeOnly(now.Hour, now.Minute),
            FollowUpContactId = followUp.ContactId,
            FollowUpTypeId = followUp.FollowUpTypeId,
            RowVersion = Convert.ToBase64String(followUp.RowVersion)
        };
        FollowUpRules.SetDefaults(model, settings, followUp.AssignedToId, null);
        await FillCompleteAsync(model, followUp, ct);
        return PartialView("_Complete", model);
    }

    [HttpPost]
    public async Task<IActionResult> Complete(int id, CompleteFollowUpViewModel model, CancellationToken ct)
    {
        var followUp = await LoadEditableAsync(id, ct);
        if (followUp == null) return NotAllowed();
        if (!followUp.IsOpen) return Fail(T["FollowUps.NotOpen"]);
        var settings = await SettingsAsync();
        model.FollowUpId = id;
        model.ClientId = followUp.ClientId;

        if (model.LogActivity)
        {
            if (!model.ActivityTypeId.HasValue) ModelState.AddModelError(nameof(model.ActivityTypeId), T["Validation.Required", T["Field.ActivityType"]]);
            if (!model.ActivityDate.HasValue) ModelState.AddModelError(nameof(model.ActivityDate), T["Validation.Required", T["Field.Date"]]);
        }
        FollowUpRules.ValidateNext(model, ModelState, settings, T);
        if (model.ScheduleFollowUp && model.FollowUpContactId.HasValue && !await Db.Contacts.AnyAsync(c => c.ContactId == model.FollowUpContactId && c.ClientId == followUp.ClientId, ct))
            ModelState.AddModelError(nameof(model.FollowUpContactId), T["Validation.WrongClient"]);
        if (!ModelState.IsValid)
        {
            await FillCompleteAsync(model, followUp, ct);
            return FormView("_Complete", model);
        }

        SetOriginalRowVersion(followUp, model.RowVersion);
        Activity? activity = null;
        if (model.LogActivity)
        {
            activity = new Activity
            {
                ClientId = followUp.ClientId,
                ContactId = followUp.ContactId,
                ActivityTypeId = model.ActivityTypeId!.Value,
                ActivityDate = model.ActivityDate!.Value,
                ActivityTime = model.ActivityTime,
                Notes = string.IsNullOrWhiteSpace(model.ActivityNotes) ? followUp.Description : model.ActivityNotes,
                Outcome = string.IsNullOrWhiteSpace(model.ActivityOutcome) ? null : model.ActivityOutcome.Trim(),
                ProjectId = followUp.ProjectId,
                QuotationId = followUp.QuotationId,
                TenderId = followUp.TenderId,
                PerformedById = UserId
            };
            Db.Activities.Add(activity);
            followUp.CompletedActivity = activity;
        }

        followUp.Status = FollowUpStatus.Completed;
        followUp.CompletedAtUtc = DateTime.UtcNow;
        followUp.CompletedById = UserId;
        followUp.CompletionNotes = string.IsNullOrWhiteSpace(model.CompletionNotes) ? null : model.CompletionNotes.Trim();
        if (followUp.ReminderStatus == ReminderStatus.Scheduled) followUp.ReminderStatus = ReminderStatus.Skipped;

        string? warning = null;
        if (model.ScheduleFollowUp)
        {
            var next = FollowUpRules.Build(model, followUp.ClientId, followUp.AssignedToId, CurrentUser.CanSeeAllData, settings);
            next.ParentFollowUp = followUp;
            next.SourceActivity = activity;
            next.ProjectId = followUp.ProjectId;
            next.QuotationId = followUp.QuotationId;
            next.TenderId = followUp.TenderId;
            Db.FollowUps.Add(next);
            warning = FollowUpRules.LateReminderWarning(next, T);
        }

        await Db.SaveChangesAsync(ct); // completion + activity + next follow-up: one transaction
        if (model.ScheduleFollowUp) _reminders.Wake();
        return Saved(T["FollowUps.Completed"], new { warning });
    }

    // ---------------------------------------------------------------- cancel
    [HttpGet]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var followUp = await LoadEditableAsync(id, ct);
        if (followUp == null) return NotAllowed();
        if (!followUp.IsOpen) return Fail(T["FollowUps.NotOpen"]);
        return PartialView("_Cancel", new CancelFollowUpViewModel
        {
            FollowUpId = id,
            Description = followUp.Description,
            ClientName = await ClientNameAsync(followUp.ClientId),
            RowVersion = Convert.ToBase64String(followUp.RowVersion)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, CancelFollowUpViewModel model, CancellationToken ct)
    {
        var followUp = await LoadEditableAsync(id, ct);
        if (followUp == null) return NotAllowed();
        if (!followUp.IsOpen) return Fail(T["FollowUps.NotOpen"]);
        if (!ModelState.IsValid)
        {
            model.FollowUpId = id;
            model.Description = followUp.Description;
            model.ClientName = await ClientNameAsync(followUp.ClientId);
            return FormView("_Cancel", model);
        }
        SetOriginalRowVersion(followUp, model.RowVersion);
        followUp.Status = FollowUpStatus.Cancelled;
        followUp.CancelledAtUtc = DateTime.UtcNow;
        followUp.CancelledById = UserId;
        followUp.CancelReason = model.CancelReason.Trim();
        if (followUp.ReminderStatus == ReminderStatus.Scheduled) followUp.ReminderStatus = ReminderStatus.Skipped;
        await Db.SaveChangesAsync(ct);
        return Saved(T["FollowUps.Cancelled"]);
    }

    // ---------------------------------------------------------------- helpers
    private async Task<FollowUp?> LoadEditableAsync(int id, CancellationToken ct)
    {
        var followUp = await Db.FollowUps.FirstOrDefaultAsync(f => f.FollowUpId == id, ct);
        if (followUp == null || !await CanAccessClientAsync(followUp.ClientId)) return null;
        var uid = UserId;
        return CurrentUser.CanSeeAllData || followUp.AssignedToId == uid || followUp.CreatedBy == uid ? followUp : null;
    }

    private async Task ValidateAsync(FollowUpFormViewModel m, SettingsSnapshot settings, CancellationToken ct)
    {
        if (m.DueDate.HasValue && m.DueTime.HasValue)
            FollowUpRules.ValidateFuture(m.DueDate.Value, m.DueTime.Value, nameof(m.DueDate), ModelState, settings, T);
        if (!m.ClientId.HasValue) return;
        if (m.ContactId.HasValue && !await Db.Contacts.AnyAsync(c => c.ContactId == m.ContactId && c.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.ContactId), T["Validation.WrongClient"]);
        if (m.ProjectId.HasValue && !await Db.Projects.AnyAsync(p => p.ProjectId == m.ProjectId && p.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.ProjectId), T["Validation.WrongClient"]);
        if (m.QuotationId.HasValue && !await Db.Quotations.AnyAsync(q => q.QuotationId == m.QuotationId && q.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.QuotationId), T["Validation.WrongClient"]);
        if (m.TenderId.HasValue && !await Db.Tenders.AnyAsync(t => t.TenderId == m.TenderId && t.ClientId == m.ClientId, ct))
            ModelState.AddModelError(nameof(m.TenderId), T["Validation.WrongClient"]);
    }

    private void Apply(FollowUpFormViewModel m, FollowUp f, SettingsSnapshot settings)
    {
        f.ClientId = m.ClientId!.Value;
        f.ContactId = m.ContactId;
        f.FollowUpTypeId = m.FollowUpTypeId!.Value;
        f.Description = m.Description.Trim();
        f.Priority = m.Priority;
        f.ProjectId = m.ProjectId;
        f.QuotationId = m.QuotationId;
        f.TenderId = m.TenderId;
        if (m.SourceActivityId.HasValue && f.FollowUpId == 0) f.SourceActivityId = m.SourceActivityId;
        f.ReminderMinutesBefore = m.ReminderMinutesBefore;
        f.AssignedToId = CurrentUser.CanSeeAllData && !string.IsNullOrEmpty(m.AssignedToId) ? m.AssignedToId : (f.FollowUpId == 0 ? UserId : f.AssignedToId);
        // Converts local Muscat date/time to UTC and re-arms the reminder when the time changes
        f.SetSchedule(m.DueDate!.Value, m.DueTime!.Value, settings.TimeZoneId);
    }

    private async Task FillFormAsync(FollowUpFormViewModel m)
    {
        var arabic = IsArabic;
        m.CanAssign = CurrentUser.CanSeeAllData;
        m.Types = await Db.ActivityTypes.AsNoTracking().Where(t => t.IsActive || t.ActivityTypeId == m.FollowUpTypeId).OrderBy(t => t.SortOrder).ToListAsync();
        if (m.ClientId.HasValue) m.ClientName = await ClientNameAsync(m.ClientId.Value);
        if (m.ContactId.HasValue) m.ContactName = await Db.Contacts.Where(c => c.ContactId == m.ContactId).Select(c => c.FullName).FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(m.AssignedToId))
            m.AssignedToName = await Db.Users.Where(u => u.Id == m.AssignedToId).Select(u => arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName).FirstOrDefaultAsync();
        if (m.ProjectId.HasValue) m.ProjectName = await Db.Projects.Where(p => p.ProjectId == m.ProjectId).Select(p => p.ProjectName).FirstOrDefaultAsync();
        if (m.QuotationId.HasValue) m.QuotationName = await Db.Quotations.Where(q => q.QuotationId == m.QuotationId).Select(q => q.QuotationNumber).FirstOrDefaultAsync();
        if (m.TenderId.HasValue) m.TenderName = await Db.Tenders.Where(t => t.TenderId == m.TenderId).Select(t => t.TenderNumber + " - " + t.Title).FirstOrDefaultAsync();
    }

    private async Task FillCompleteAsync(CompleteFollowUpViewModel m, FollowUp f, CancellationToken ct)
    {
        var arabic = IsArabic;
        m.Types = await Db.ActivityTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToListAsync(ct);
        m.ClientName = await ClientNameAsync(f.ClientId);
        m.Description = f.Description;
        if (m.FollowUpContactId.HasValue) m.FollowUpContactName = await Db.Contacts.Where(c => c.ContactId == m.FollowUpContactId).Select(c => c.FullName).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrEmpty(m.FollowUpAssignedToId))
            m.FollowUpAssignedToName = await Db.Users.Where(u => u.Id == m.FollowUpAssignedToId).Select(u => arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName).FirstOrDefaultAsync(ct);
    }

    private Task<string?> ClientNameAsync(int clientId)
    {
        var arabic = IsArabic;
        return Db.Clients.Where(c => c.ClientId == clientId).Select(c => arabic && c.CompanyNameAr != null ? c.CompanyNameAr : c.CompanyName).FirstOrDefaultAsync();
    }
}
