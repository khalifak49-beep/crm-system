using CrmSystem.Data;
using CrmSystem.Infrastructure;
using CrmSystem.Infrastructure.Localization;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;
using CrmSystem.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// Dashboard. Every number comes from SQL Server - nothing is hard-coded.
///
/// Scope rules:
///   Admin / Sales Manager : all data, optional filter by employee.
///   Sales Employee        : only his own activities, follow-ups and projects,
///                           and only clients he can access.
/// Date filter applies to activities (ActivityDate) and to Won/Lost projects (ClosedDate).
/// Follow-up counters and the pipeline show the CURRENT state (not date filtered).
/// </summary>
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAppSettings _settings;

    public DashboardController(ApplicationDbContext db, ICurrentUser user, IAppSettings settings)
    {
        _db = db;
        _user = user;
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        return View(new DashboardPageViewModel
        {
            CanSeeAllData = _user.CanSeeAllData,
            TodayLocal = settings.TodayLocal.ToString("yyyy-MM-dd"),
            CurrentUserId = _user.UserId
        });
    }

    /// <summary>
    /// GET /Dashboard/Data?from=2026-01-01&amp;to=2026-11-10&amp;clientId=5&amp;userId=...&amp;granularity=month
    /// granularity: day | month | year | auto (default)
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Data(DateOnly? from, DateOnly? to, int? clientId, string? userId, string? granularity, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        var today = settings.TodayLocal;

        // ---------- Normalize filters ----------
        var dateFrom = from ?? new DateOnly(today.Year, 1, 1);
        var dateTo = to ?? today;
        if (dateTo < dateFrom) (dateFrom, dateTo) = (dateTo, dateFrom);
        if (dateTo.DayNumber - dateFrom.DayNumber > 3660) dateFrom = dateTo.AddYears(-10);
        var days = dateTo.DayNumber - dateFrom.DayNumber + 1;

        var gran = granularity switch
        {
            "day" when days <= 366 => "day",
            "day" => "month",
            "month" => "month",
            "year" => "year",
            _ => days <= 62 ? "day" : days <= 1100 ? "month" : "year"
        };

        var employeeId = _user.EffectiveEmployeeFilter(userId);

        // A client filter must be a client this user may see
        if (clientId.HasValue)
        {
            var visible = await _db.Clients.AsNoTracking().VisibleTo(_user, settings).AnyAsync(c => c.ClientId == clientId.Value, ct);
            if (!visible) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Client not accessible." });
        }

        // ---------- Activities in range ----------
        var activitiesAllEmployees = _db.Activities.AsNoTracking()
            .Where(a => a.ActivityDate >= dateFrom && a.ActivityDate <= dateTo);
        if (clientId.HasValue)
            activitiesAllEmployees = activitiesAllEmployees.Where(a => a.ClientId == clientId.Value);
        if (!_user.CanSeeAllData)
            activitiesAllEmployees = activitiesAllEmployees.Where(a => a.PerformedById == _user.UserId);

        var activities = employeeId != null
            ? activitiesAllEmployees.Where(a => a.PerformedById == employeeId)
            : activitiesAllEmployees;

        var byType = await activities
            .GroupBy(a => a.ActivityTypeId)
            .Select(g => new { TypeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TypeId, x => x.Count, ct);
        int TypeCount(int typeId) => byType.TryGetValue(typeId, out var c) ? c : 0;

        // ---------- Clients ----------
        var clients = _db.Clients.AsNoTracking().VisibleTo(_user, settings);
        if (employeeId != null && _user.CanSeeAllData)
            clients = clients.OwnedOrAssignedTo(employeeId);
        if (clientId.HasValue)
            clients = clients.Where(c => c.ClientId == clientId.Value);

        // ---------- Follow-ups (current state) ----------
        var openFollowUps = _db.FollowUps.AsNoTracking()
            .Where(f => f.Status == FollowUpStatus.Pending || f.Status == FollowUpStatus.Overdue);
        if (clientId.HasValue) openFollowUps = openFollowUps.Where(f => f.ClientId == clientId.Value);
        if (employeeId != null) openFollowUps = openFollowUps.Where(f => f.AssignedToId == employeeId);

        var nowUtc = DateTime.UtcNow;
        var (todayStartUtc, todayEndUtc) = settings.LocalDayRangeUtc(today);

        // ---------- Projects ----------
        var projects = _db.Projects.AsNoTracking().AsQueryable();
        if (clientId.HasValue) projects = projects.Where(p => p.ClientId == clientId.Value);
        if (employeeId != null) projects = projects.Where(p => p.AssignedToId == employeeId);
        else if (!_user.CanSeeAllData) projects = projects.Where(p => p.AssignedToId == _user.UserId);

        var projectsByStatus = await projects
            .GroupBy(p => p.ProjectStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var closedInRange = projects.Where(p =>
            (p.ClosedDate ?? DateOnly.FromDateTime(p.CreatedAt)) >= dateFrom &&
            (p.ClosedDate ?? DateOnly.FromDateTime(p.CreatedAt)) <= dateTo);

        var stats = new DashboardStats
        {
            TotalClients = await clients.CountAsync(ct),
            ActiveClients = await clients.CountAsync(c => c.Status == ClientStatus.Active, ct),
            Calls = TypeCount(ActivityTypeIds.ColdCall),
            Emails = TypeCount(ActivityTypeIds.Email),
            Visits = TypeCount(ActivityTypeIds.Visit),
            FacilityTours = TypeCount(ActivityTypeIds.FacilityTour),
            Meetings = TypeCount(ActivityTypeIds.Meeting),
            UpcomingFollowUps = await openFollowUps.CountAsync(f => f.DueAtUtc >= nowUtc, ct),
            OverdueFollowUps = await openFollowUps.CountAsync(f => f.DueAtUtc < nowUtc, ct),
            TodayFollowUps = await openFollowUps.CountAsync(f => f.DueAtUtc >= todayStartUtc && f.DueAtUtc < todayEndUtc, ct),
            PotentialProjects = projectsByStatus.Where(p => p.Key >= ProjectStatus.New && p.Key <= ProjectStatus.Negotiation).Sum(p => p.Value),
            WonProjects = await closedInRange.CountAsync(p => p.ProjectStatus == ProjectStatus.Won, ct),
            LostProjects = await closedInRange.CountAsync(p => p.ProjectStatus == ProjectStatus.Lost, ct)
        };

        // ---------- Activities over time + calls/emails/visits comparison ----------
        var buckets = BuildBuckets(dateFrom, dateTo, gran);
        var rows = await LoadBucketRowsAsync(activities, gran, ct);

        var timeline = buckets.Select(b => rows.Where(r => r.Bucket == b).Sum(r => r.Count)).ToList();
        int[] Series(int typeId) => buckets.Select(b => rows.Where(r => r.Bucket == b && r.TypeId == typeId).Sum(r => r.Count)).ToArray();

        // ---------- Most contacted clients ----------
        var arabic = AppCultures.IsArabic;
        var topClientCounts = await activities
            .GroupBy(a => a.ClientId)
            .Select(g => new { ClientId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);
        var topClientIds = topClientCounts.Select(x => x.ClientId).ToList();
        var clientNames = await _db.Clients.IgnoreQueryFilters().AsNoTracking()
            .Where(c => topClientIds.Contains(c.ClientId))
            .Select(c => new { c.ClientId, Name = arabic && c.CompanyNameAr != null ? c.CompanyNameAr : c.CompanyName })
            .ToDictionaryAsync(c => c.ClientId, c => c.Name, ct);
        var topClients = topClientCounts
            .Select(x => new ChartPoint(clientNames.GetValueOrDefault(x.ClientId, "#" + x.ClientId), x.Count, x.ClientId.ToString()))
            .ToList();

        // ---------- Employee performance (managers / admins only) ----------
        List<ChartPoint>? employeePerformance = null;
        if (_user.CanSeeAllData)
        {
            var perf = await activitiesAllEmployees
                .GroupBy(a => a.PerformedById)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(15)
                .ToListAsync(ct);
            var ids = perf.Select(p => p.UserId).ToList();
            var names = await _db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, Name = arabic && u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName })
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);
            employeePerformance = perf.Select(p => new ChartPoint(names.GetValueOrDefault(p.UserId, "?"), p.Count, p.UserId)).ToList();
        }

        return Json(new
        {
            range = new { from = dateFrom.ToString("yyyy-MM-dd"), to = dateTo.ToString("yyyy-MM-dd"), granularity = gran },
            stats,
            timeline = new { labels = buckets, values = timeline },
            comparison = new
            {
                labels = buckets,
                calls = Series(ActivityTypeIds.ColdCall),
                emails = Series(ActivityTypeIds.Email),
                visits = Series(ActivityTypeIds.Visit)
            },
            topClients,
            projectsByStatus = Enum.GetValues<ProjectStatus>()
                .Select(s => new { status = (int)s, count = projectsByStatus.GetValueOrDefault(s, 0) }),
            employeePerformance,
            generatedAtLocal = settings.NowLocal.ToString("yyyy-MM-ddTHH:mm:ss")
        });
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private sealed record BucketRow(string Bucket, int TypeId, int Count);

    /// <summary>All bucket keys in the range (so empty periods show as 0).</summary>
    private static List<string> BuildBuckets(DateOnly from, DateOnly to, string granularity)
    {
        var list = new List<string>();
        switch (granularity)
        {
            case "day":
                for (var d = from; d <= to; d = d.AddDays(1)) list.Add(d.ToString("yyyy-MM-dd"));
                break;
            case "month":
                for (var d = new DateOnly(from.Year, from.Month, 1); d <= to; d = d.AddMonths(1)) list.Add(d.ToString("yyyy-MM"));
                break;
            default:
                for (var y = from.Year; y <= to.Year; y++) list.Add(y.ToString("0000"));
                break;
        }
        return list;
    }

    /// <summary>Counts per (bucket, activity type), grouped in SQL Server.</summary>
    private static async Task<List<BucketRow>> LoadBucketRowsAsync(IQueryable<Activity> activities, string granularity, CancellationToken ct)
    {
        switch (granularity)
        {
            case "day":
                var byDay = await activities
                    .GroupBy(a => new { a.ActivityDate, a.ActivityTypeId })
                    .Select(g => new { g.Key.ActivityDate, g.Key.ActivityTypeId, Count = g.Count() })
                    .ToListAsync(ct);
                return byDay.Select(r => new BucketRow(r.ActivityDate.ToString("yyyy-MM-dd"), r.ActivityTypeId, r.Count)).ToList();

            case "month":
                var byMonth = await activities
                    .GroupBy(a => new { a.ActivityDate.Year, a.ActivityDate.Month, a.ActivityTypeId })
                    .Select(g => new { g.Key.Year, g.Key.Month, g.Key.ActivityTypeId, Count = g.Count() })
                    .ToListAsync(ct);
                return byMonth.Select(r => new BucketRow($"{r.Year:0000}-{r.Month:00}", r.ActivityTypeId, r.Count)).ToList();

            default:
                var byYear = await activities
                    .GroupBy(a => new { a.ActivityDate.Year, a.ActivityTypeId })
                    .Select(g => new { g.Key.Year, g.Key.ActivityTypeId, Count = g.Count() })
                    .ToListAsync(ct);
                return byYear.Select(r => new BucketRow($"{r.Year:0000}", r.ActivityTypeId, r.Count)).ToList();
        }
    }
}
