using System.Reflection;
using ClosedXML.Excel;
using CrmSystem.Data;
using CrmSystem.Infrastructure;
using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CrmSystem.Services;

/// <summary>One column of a report. Kind drives formatting (screen, Excel, print).</summary>
public sealed record ReportColumn(string Key, string TitleKey, string Kind);

public sealed class ReportFilter
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int? ClientId { get; set; }
    public string? UserId { get; set; }
    public int? ActivityTypeId { get; set; }
    public FollowUpStatus? FollowUpStatus { get; set; }
    public ProjectStatus? ProjectStatus { get; set; }
}

public sealed class ReportPage
{
    public List<ReportColumn> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
}

/// <summary>
/// Builds the nine reports from SQL Server (all filtering, sorting and paging in SQL).
/// Kinds: text | date | time | number | money | percent | status:ClientStatus | status:ProjectStatus |
///        status:FollowUpStatus | status:Priority
/// Scope: Admin / Sales Manager see everything; Sales Employees see only their own data.
/// </summary>
public sealed class ReportService
{
    public static readonly string[] Types = { "activities", "calls", "emails", "visits", "followups", "overdue", "clients", "projects", "employees" };

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public ReportService(ApplicationDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<ReportPage> RunAsync(string type, ReportFilter f, GridRequest grid, SettingsSnapshot settings, bool arabic, bool allRows, CancellationToken ct)
    {
        var visible = _db.Clients.VisibleTo(_user, settings);
        // employees are always limited to themselves
        var userId = _user.CanSeeAllData ? (string.IsNullOrEmpty(f.UserId) ? null : f.UserId) : _user.UserId;

        return type switch
        {
            "calls" => await ActivitiesAsync(f, grid, visible, userId, new[] { ActivityTypeIds.ColdCall }, arabic, allRows, ct),
            "emails" => await ActivitiesAsync(f, grid, visible, userId, new[] { ActivityTypeIds.Email }, arabic, allRows, ct),
            "visits" => await ActivitiesAsync(f, grid, visible, userId, new[] { ActivityTypeIds.Visit, ActivityTypeIds.FacilityTour }, arabic, allRows, ct),
            "followups" => await FollowUpsAsync(f, grid, visible, userId, overdueOnly: false, arabic, allRows, ct),
            "overdue" => await FollowUpsAsync(f, grid, visible, userId, overdueOnly: true, arabic, allRows, ct),
            "clients" => await ClientsAsync(f, grid, visible, userId, allRows, ct),
            "projects" => await ProjectsAsync(f, grid, visible, userId, arabic, allRows, ct),
            "employees" => await EmployeesAsync(f, visible, userId, arabic, ct),
            _ => await ActivitiesAsync(f, grid, visible, userId, f.ActivityTypeId.HasValue ? new[] { f.ActivityTypeId.Value } : Array.Empty<int>(), arabic, allRows, ct)
        };
    }

    // ------------------------------------------------------------------ activities / calls / emails / visits
    public sealed class ActivityReportRow
    {
        public int ActivityId { get; set; }
        public DateOnly ActivityDate { get; set; }
        public TimeOnly? ActivityTime { get; set; }
        public string CompanyName { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string? ContactName { get; set; }
        public string? Subject { get; set; }
        public string? Notes { get; set; }
        public string? Outcome { get; set; }
        public string PerformedBy { get; set; } = "";
        public string? ProjectName { get; set; }
    }

    private async Task<ReportPage> ActivitiesAsync(ReportFilter f, GridRequest grid, IQueryable<Models.Entities.Client> visible, string? userId, int[] types, bool arabic, bool allRows, CancellationToken ct)
    {
        var q = _db.Activities.AsNoTracking().Where(a => visible.Any(v => v.ClientId == a.ClientId));
        if (types.Length > 0) q = q.Where(a => types.Contains(a.ActivityTypeId));
        if (f.From.HasValue) q = q.Where(a => a.ActivityDate >= f.From.Value);
        if (f.To.HasValue) q = q.Where(a => a.ActivityDate <= f.To.Value);
        if (f.ClientId.HasValue) q = q.Where(a => a.ClientId == f.ClientId.Value);
        if (userId != null) q = q.Where(a => a.PerformedById == userId);
        if (grid.SearchTerm is { } s) q = q.Where(a => (a.Notes != null && a.Notes.Contains(s)) || (a.Outcome != null && a.Outcome.Contains(s)) || a.Client.CompanyName.Contains(s));

        var rows = q.Select(a => new ActivityReportRow
        {
            ActivityId = a.ActivityId,
            ActivityDate = a.ActivityDate,
            ActivityTime = a.ActivityTime,
            CompanyName = arabic && a.Client.CompanyNameAr != null ? a.Client.CompanyNameAr : a.Client.CompanyName,
            TypeName = arabic ? a.ActivityType.NameAr : a.ActivityType.NameEn,
            ContactName = a.Contact != null ? a.Contact.FullName : null,
            Subject = a.Subject,
            Notes = a.Notes,
            Outcome = a.Outcome,
            PerformedBy = a.PerformedBy.FullName,
            ProjectName = a.Project != null ? a.Project.ProjectName : null
        }).ApplySort(grid, nameof(ActivityReportRow.ActivityDate), defaultDescending: true, tieBreaker: nameof(ActivityReportRow.ActivityId));

        return await PageAsync(rows, grid, allRows, new()
        {
            new("activityDate", "Field.Date", "date"), new("activityTime", "Field.Time", "time"),
            new("companyName", "Field.Company", "text"), new("typeName", "Field.Type", "text"),
            new("contactName", "Field.Contact", "text"), new("subject", "Field.Subject", "text"),
            new("notes", "Field.Notes", "longtext"), new("outcome", "Field.Outcome", "text"),
            new("performedBy", "Field.PerformedBy", "text"), new("projectName", "Field.Project", "text")
        }, ct);
    }

    // ------------------------------------------------------------------ follow-ups / overdue
    public sealed class FollowUpReportRow
    {
        public int FollowUpId { get; set; }
        public DateOnly DueDate { get; set; }
        public TimeOnly DueTime { get; set; }
        public DateTime DueAtUtc { get; set; }
        public string CompanyName { get; set; } = "";
        public string? ContactName { get; set; }
        public string TypeName { get; set; } = "";
        public string Description { get; set; } = "";
        public string AssignedTo { get; set; } = "";
        public int Priority { get; set; }
        public int Status { get; set; }
        public int DaysOverdue { get; set; }
        public string? CompletionNotes { get; set; }
    }

    private async Task<ReportPage> FollowUpsAsync(ReportFilter f, GridRequest grid, IQueryable<Models.Entities.Client> visible, string? userId, bool overdueOnly, bool arabic, bool allRows, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var q = _db.FollowUpDetails.AsNoTracking().Where(x => visible.Any(v => v.ClientId == x.ClientId));
        if (overdueOnly) q = q.Where(x => (x.Status == FollowUpStatus.Pending || x.Status == FollowUpStatus.Overdue) && x.DueAtUtc < now);
        else if (f.FollowUpStatus.HasValue) q = q.Where(x => x.EffectiveStatus == f.FollowUpStatus.Value);
        if (f.From.HasValue) q = q.Where(x => x.DueDate >= f.From.Value);
        if (f.To.HasValue) q = q.Where(x => x.DueDate <= f.To.Value);
        if (f.ClientId.HasValue) q = q.Where(x => x.ClientId == f.ClientId.Value);
        if (userId != null) q = q.Where(x => x.AssignedToId == userId);
        if (grid.SearchTerm is { } s) q = q.Where(x => x.Description.Contains(s) || x.CompanyName.Contains(s));

        var rows = q.Select(x => new FollowUpReportRow
        {
            FollowUpId = x.FollowUpId,
            DueDate = x.DueDate,
            DueTime = x.DueTime,
            DueAtUtc = x.DueAtUtc,
            CompanyName = arabic && x.CompanyNameAr != null ? x.CompanyNameAr : x.CompanyName,
            ContactName = x.ContactName,
            TypeName = arabic ? x.FollowUpTypeNameAr : x.FollowUpTypeNameEn,
            Description = x.Description,
            AssignedTo = x.AssignedToName,
            Priority = (int)x.Priority,
            Status = (int)x.EffectiveStatus,
            DaysOverdue = 0, // filled below (date math done in memory)
            CompletionNotes = x.CompletionNotes ?? x.CancelReason
        }).ApplySort(grid, nameof(FollowUpReportRow.DueAtUtc), defaultDescending: false, tieBreaker: nameof(FollowUpReportRow.FollowUpId));

        var columns = new List<ReportColumn>
        {
            new("dueDate", "Field.Date", "date"), new("dueTime", "Field.Time", "time"),
            new("companyName", "Field.Company", "text"), new("contactName", "Field.Contact", "text"),
            new("typeName", "Field.Type", "text"), new("description", "Field.Description", "longtext"),
            new("assignedTo", "Field.AssignedTo", "text"), new("priority", "Field.Priority", "status:Priority"),
            new("status", "Field.Status", "status:FollowUpStatus")
        };
        columns.Add(overdueOnly ? new("daysOverdue", "Reports.DaysOverdue", "number") : new("completionNotes", "Field.CompletionNotes", "text"));
        var page = await PageAsync(rows, grid, allRows, columns, ct);
        foreach (var item in page.Items)
            if (item["status"] is int st && st == (int)FollowUpStatus.Overdue && item["dueDate"] is DateOnly due)
                item["daysOverdue"] = Math.Max(0, today.DayNumber - due.DayNumber);
        return page;
    }

    // ------------------------------------------------------------------ clients
    public sealed class ClientReportRow
    {
        public int ClientId { get; set; }
        public string CompanyCode { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string? Industry { get; set; }
        public string? City { get; set; }
        public int Status { get; set; }
        public string? AccountManager { get; set; }
        public string? PrimaryContact { get; set; }
        public DateOnly? LastActivityDate { get; set; }
        public DateOnly? NextFollowUpDate { get; set; }
        public int ActivitiesInPeriod { get; set; }
        public long TotalActivities { get; set; }
        public long OpenProjects { get; set; }
        public long OverdueFollowUps { get; set; }
    }

    private async Task<ReportPage> ClientsAsync(ReportFilter f, GridRequest grid, IQueryable<Models.Entities.Client> visible, string? userId, bool allRows, CancellationToken ct)
    {
        var from = f.From ?? DateOnly.MinValue;
        var to = f.To ?? DateOnly.MaxValue;
        var q = _db.ClientSummaries.AsNoTracking().Where(s => visible.Any(v => v.ClientId == s.ClientId));
        if (f.ClientId.HasValue) q = q.Where(s => s.ClientId == f.ClientId.Value);
        if (userId != null) q = q.Where(s => s.AccountManagerId == userId);
        if (grid.SearchTerm is { } term) q = q.Where(s => s.CompanyName.Contains(term) || s.CompanyCode.Contains(term));

        var rows = q.Select(s => new ClientReportRow
        {
            ClientId = s.ClientId,
            CompanyCode = s.CompanyCode,
            CompanyName = s.CompanyName,
            Industry = s.Industry,
            City = s.City,
            Status = (int)s.Status,
            AccountManager = s.AccountManagerName,
            PrimaryContact = s.PrimaryContactName,
            LastActivityDate = s.LastActivityDate,
            NextFollowUpDate = s.NextFollowUpDate,
            ActivitiesInPeriod = _db.Activities.Count(a => a.ClientId == s.ClientId && a.ActivityDate >= from && a.ActivityDate <= to),
            TotalActivities = s.TotalActivities,
            OpenProjects = s.OpenProjects,
            OverdueFollowUps = s.OverdueFollowUps
        }).ApplySort(grid, nameof(ClientReportRow.CompanyName), tieBreaker: nameof(ClientReportRow.ClientId));

        return await PageAsync(rows, grid, allRows, new()
        {
            new("companyCode", "Field.CompanyCode", "text"), new("companyName", "Field.CompanyName", "text"),
            new("industry", "Field.Industry", "text"), new("city", "Field.City", "text"),
            new("status", "Field.Status", "status:ClientStatus"), new("accountManager", "Field.AccountManager", "text"),
            new("primaryContact", "Field.PrimaryContact", "text"), new("lastActivityDate", "Clients.LastContact", "date"),
            new("nextFollowUpDate", "Clients.NextFollowUp", "date"), new("activitiesInPeriod", "Reports.ActivitiesInPeriod", "number"),
            new("totalActivities", "Reports.TotalActivities", "number"), new("openProjects", "Clients.OpenProjects", "number"),
            new("overdueFollowUps", "Stats.OverdueFollowUps", "number")
        }, ct);
    }

    // ------------------------------------------------------------------ projects
    public sealed class ProjectReportRow
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public int Status { get; set; }
        public decimal? ProjectValue { get; set; }
        public string Currency { get; set; } = "";
        public byte Probability { get; set; }
        public decimal? WeightedValue { get; set; }
        public DateOnly? ExpectedStartDate { get; set; }
        public DateOnly? ExpectedEndDate { get; set; }
        public DateOnly? ClosedDate { get; set; }
        public string? AssignedTo { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private async Task<ReportPage> ProjectsAsync(ReportFilter f, GridRequest grid, IQueryable<Models.Entities.Client> visible, string? userId, bool arabic, bool allRows, CancellationToken ct)
    {
        var q = _db.Projects.AsNoTracking().Where(p => visible.Any(v => v.ClientId == p.ClientId));
        if (f.ProjectStatus.HasValue) q = q.Where(p => p.ProjectStatus == f.ProjectStatus.Value);
        if (f.From.HasValue) q = q.Where(p => DateOnly.FromDateTime(p.CreatedAt) >= f.From.Value || (p.ClosedDate != null && p.ClosedDate >= f.From.Value));
        if (f.To.HasValue) q = q.Where(p => DateOnly.FromDateTime(p.CreatedAt) <= f.To.Value);
        if (f.ClientId.HasValue) q = q.Where(p => p.ClientId == f.ClientId.Value);
        if (userId != null) q = q.Where(p => p.AssignedToId == userId);
        if (grid.SearchTerm is { } s) q = q.Where(p => p.ProjectName.Contains(s) || p.Client.CompanyName.Contains(s));

        var rows = q.Select(p => new ProjectReportRow
        {
            ProjectId = p.ProjectId,
            ProjectName = p.ProjectName,
            CompanyName = arabic && p.Client.CompanyNameAr != null ? p.Client.CompanyNameAr : p.Client.CompanyName,
            Status = (int)p.ProjectStatus,
            ProjectValue = p.ProjectValue,
            Currency = p.Currency,
            Probability = p.Probability,
            WeightedValue = p.ProjectValue * p.Probability / 100m,
            ExpectedStartDate = p.ExpectedStartDate,
            ExpectedEndDate = p.ExpectedEndDate,
            ClosedDate = p.ClosedDate,
            AssignedTo = p.AssignedTo != null ? p.AssignedTo.FullName : null,
            CreatedAt = p.CreatedAt
        }).ApplySort(grid, nameof(ProjectReportRow.CreatedAt), defaultDescending: true, tieBreaker: nameof(ProjectReportRow.ProjectId));

        return await PageAsync(rows, grid, allRows, new()
        {
            new("projectName", "Field.ProjectName", "text"), new("companyName", "Field.Company", "text"),
            new("status", "Field.Status", "status:ProjectStatus"), new("projectValue", "Field.Value", "money"),
            new("probability", "Field.Probability", "percent"), new("weightedValue", "Reports.WeightedValue", "money"),
            new("expectedStartDate", "Field.ExpectedStartDate", "date"), new("expectedEndDate", "Field.ExpectedEndDate", "date"),
            new("closedDate", "Field.ClosedDate", "date"), new("assignedTo", "Field.AssignedTo", "text")
        }, ct);
    }

    // ------------------------------------------------------------------ employee performance
    private async Task<ReportPage> EmployeesAsync(ReportFilter f, IQueryable<Models.Entities.Client> visible, string? userId, bool arabic, CancellationToken ct)
    {
        var from = f.From ?? DateOnly.MinValue;
        var to = f.To ?? DateOnly.MaxValue;
        var now = DateTime.UtcNow;

        var users = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && (userId == null || u.Id == userId))
            .Select(u => new { u.Id, Name = arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName, u.JobTitle })
            .ToListAsync(ct);

        var acts = _db.Activities.AsNoTracking().Where(a => a.ActivityDate >= from && a.ActivityDate <= to && visible.Any(v => v.ClientId == a.ClientId));
        if (f.ClientId.HasValue) acts = acts.Where(a => a.ClientId == f.ClientId.Value);
        var actCounts = await acts.GroupBy(a => new { a.PerformedById, a.ActivityTypeId })
            .Select(g => new { g.Key.PerformedById, g.Key.ActivityTypeId, Count = g.Count() }).ToListAsync(ct);

        var completed = await _db.FollowUps.AsNoTracking()
            .Where(x => x.Status == FollowUpStatus.Completed && x.CompletedAtUtc != null
                        && DateOnly.FromDateTime(x.CompletedAtUtc.Value) >= from && DateOnly.FromDateTime(x.CompletedAtUtc.Value) <= to)
            .GroupBy(x => x.AssignedToId).Select(g => new { UserId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
        var overdue = await _db.FollowUps.AsNoTracking()
            .Where(x => (x.Status == FollowUpStatus.Pending || x.Status == FollowUpStatus.Overdue) && x.DueAtUtc < now)
            .GroupBy(x => x.AssignedToId).Select(g => new { UserId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
        var won = await _db.Projects.AsNoTracking()
            .Where(p => p.ProjectStatus == ProjectStatus.Won && p.ClosedDate >= from && p.ClosedDate <= to && p.AssignedToId != null)
            .GroupBy(p => p.AssignedToId!).Select(g => new { UserId = g.Key, Count = g.Count(), Value = g.Sum(p => p.ProjectValue ?? 0) })
            .ToDictionaryAsync(x => x.UserId, x => (x.Count, x.Value), ct);

        int C(string uid, params int[] types) => actCounts.Where(a => a.PerformedById == uid && (types.Length == 0 || types.Contains(a.ActivityTypeId))).Sum(a => a.Count);

        var items = users.Select(u => new Dictionary<string, object?>
        {
            ["employee"] = u.Name,
            ["jobTitle"] = u.JobTitle,
            ["calls"] = C(u.Id, ActivityTypeIds.ColdCall),
            ["emails"] = C(u.Id, ActivityTypeIds.Email),
            ["visits"] = C(u.Id, ActivityTypeIds.Visit),
            ["facilityTours"] = C(u.Id, ActivityTypeIds.FacilityTour),
            ["meetings"] = C(u.Id, ActivityTypeIds.Meeting),
            ["totalActivities"] = C(u.Id),
            ["followUpsCompleted"] = completed.GetValueOrDefault(u.Id),
            ["followUpsOverdue"] = overdue.GetValueOrDefault(u.Id),
            ["projectsWon"] = won.TryGetValue(u.Id, out var w) ? w.Count : 0,
            ["wonValue"] = won.TryGetValue(u.Id, out var wv) ? wv.Value : 0m
        })
        .OrderByDescending(d => (int)d["totalActivities"]!)
        .ToList();

        return new ReportPage
        {
            Columns = new()
            {
                new("employee", "Reports.Employee", "text"), new("jobTitle", "Field.JobTitle", "text"),
                new("calls", "Stats.Calls", "number"), new("emails", "Stats.Emails", "number"),
                new("visits", "Stats.Visits", "number"), new("facilityTours", "Stats.FacilityTours", "number"),
                new("meetings", "Stats.Meetings", "number"), new("totalActivities", "Reports.TotalActivities", "number"),
                new("followUpsCompleted", "Reports.FollowUpsCompleted", "number"), new("followUpsOverdue", "Stats.OverdueFollowUps", "number"),
                new("projectsWon", "Stats.WonProjects", "number"), new("wonValue", "Reports.WonValue", "money")
            },
            Items = items,
            Total = items.Count,
            Page = 1,
            PageSize = Math.Max(items.Count, 1)
        };
    }

    // ------------------------------------------------------------------ helpers
    private static async Task<ReportPage> PageAsync<T>(IQueryable<T> rows, GridRequest grid, bool allRows, List<ReportColumn> columns, CancellationToken ct)
    {
        if (allRows)
        {
            var all = await rows.Take(50000).ToListAsync(ct);
            return new ReportPage { Columns = columns, Items = all.Select(ToDictionary).ToList(), Total = all.Count, PageSize = all.Count };
        }
        var page = await rows.ToGridResultAsync(grid, ct);
        return new ReportPage { Columns = columns, Items = page.Items.Select(ToDictionary).ToList(), Total = page.Total, Page = page.Page, PageSize = page.PageSize };
    }

    private static Dictionary<string, object?> ToDictionary<T>(T row)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            dict[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = p.GetValue(row);
        return dict;
    }

    // ------------------------------------------------------------------ Excel export (ClosedXML)
    public static byte[] ToExcel(ReportPage report, string title, IEnumerable<string> filterLines, IStringLocalizer l, bool arabic, string currency)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(title.Length > 31 ? title[..31] : title);
        ws.RightToLeft = arabic;

        ws.Cell(1, 1).Value = title;
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        var row = 2;
        foreach (var line in filterLines)
        {
            ws.Cell(row, 1).Value = line;
            ws.Cell(row, 1).Style.Font.SetFontColor(XLColor.Gray);
            row++;
        }
        row++;

        var headerRow = row;
        for (var c = 0; c < report.Columns.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = l[report.Columns[c].TitleKey].Value;
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#EAF2FE")).Border.SetBottomBorder(XLBorderStyleValues.Thin);
        }

        foreach (var item in report.Items)
        {
            row++;
            for (var c = 0; c < report.Columns.Count; c++)
            {
                var col = report.Columns[c];
                var cell = ws.Cell(row, c + 1);
                var value = item.GetValueOrDefault(col.Key);
                switch (value)
                {
                    case null: break;
                    case DateOnly d:
                        cell.Value = d.ToDateTime(TimeOnly.MinValue);
                        cell.Style.DateFormat.Format = "dd/mm/yyyy";
                        break;
                    case TimeOnly t:
                        cell.Value = t.ToString("hh:mm tt", arabic ? Infrastructure.Localization.AppCultures.ArabicCulture : Infrastructure.Localization.AppCultures.EnglishCulture);
                        break;
                    case DateTime dt:
                        cell.Value = dt;
                        cell.Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
                        break;
                    default:
                        if (col.Kind.StartsWith("status:"))
                            cell.Value = l[$"{col.Kind["status:".Length..]}.{value}"].Value;
                        else if (col.Kind is "number" or "money" or "percent")
                        {
                            cell.Value = Convert.ToDouble(value);
                            cell.Style.NumberFormat.Format = col.Kind == "money" ? "#,##0.000" : col.Kind == "percent" ? "0\"%\"" : "#,##0";
                        }
                        else
                        {
                            cell.Value = value.ToString();
                            if (col.Kind == "longtext") cell.Style.Alignment.SetWrapText(true);
                        }
                        break;
                }
            }
        }

        var range = ws.Range(headerRow, 1, Math.Max(row, headerRow), report.Columns.Count);
        range.SetAutoFilter();
        ws.SheetView.FreezeRows(headerRow);
        ws.Columns(1, report.Columns.Count).AdjustToContents(headerRow, row, 10, 60);
        for (var c = 0; c < report.Columns.Count; c++)
            if (report.Columns[c].Kind == "longtext") ws.Column(c + 1).Width = 60;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }
}
