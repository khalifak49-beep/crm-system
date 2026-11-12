using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// In-app notifications. A user can only ever read or change his own notifications.
/// Notifications are created by Services/ReminderEngine (follow-up reminders,
/// overdue follow-ups, quotation expiry, tender closing).
/// </summary>
public class NotificationsController : AppController
{
    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>GET /Notifications/Bell : unread count + latest 10.</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Bell(CancellationToken ct)
    {
        var userId = UserId;
        var unreadCount = await Db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
        var items = await Project(Db.Notifications.AsNoTracking().Where(n => n.UserId == userId))
            .OrderByDescending(n => n.Id).Take(10).ToListAsync(ct);
        return Json(new { unreadCount, items });
    }

    /// <summary>GET /Notifications/List : grid of my notifications (filter: unread / type).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GridRequest grid, bool unreadOnly = false, NotificationType? type = null, CancellationToken ct = default)
    {
        var userId = UserId;
        var query = Db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => !n.IsRead);
        if (type.HasValue) query = query.Where(n => n.NotificationType == type.Value);
        if (grid.SearchTerm is { } q) query = query.Where(n => n.Title.Contains(q) || n.Message.Contains(q));
        return Json(await Project(query).ApplySort(grid, nameof(NotificationRow.Id), defaultDescending: true).ToGridResultAsync(grid, ct));
    }

    public sealed class NotificationRow
    {
        public long Id { get; set; }
        public int Type { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Url { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? EventAtLocal { get; set; }
        public string? CompanyName { get; set; }
    }

    private IQueryable<NotificationRow> Project(IQueryable<Models.Entities.Notification> query)
    {
        var arabic = IsArabic;
        return query.Select(n => new NotificationRow
        {
            Id = n.NotificationId,
            Type = (int)n.NotificationType,
            Title = n.Title,
            Message = n.Message,
            Url = n.Url,
            IsRead = n.IsRead,
            CreatedAtUtc = n.CreatedAtUtc,
            EventAtLocal = n.EventAtLocal,
            CompanyName = n.Client == null ? null : arabic && n.Client.CompanyNameAr != null ? n.Client.CompanyNameAr : n.Client.CompanyName
        });
    }

    [HttpPost]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        var userId = UserId;
        var notification = await Db.Notifications.FirstOrDefaultAsync(n => n.NotificationId == id && n.UserId == userId, ct);
        if (notification == null) return NotFoundJson();
        notification.MarkAsRead();
        await Db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var userId = UserId;
        var now = DateTime.UtcNow;
        var updated = await Db.Notifications.Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAtUtc, now), ct);
        return Json(new { ok = true, updated, message = T["Notif.AllRead"].Value });
    }

    public sealed class BrowserShownRequest { public List<long> Ids { get; set; } = new(); }

    /// <summary>The browser displayed these notifications as desktop notifications -> Browser channel = Sent.</summary>
    [HttpPost]
    public async Task<IActionResult> BrowserShown([FromBody] BrowserShownRequest request, CancellationToken ct)
    {
        var userId = UserId;
        var ids = request.Ids.Take(50).ToList();
        var now = DateTime.UtcNow;
        var updated = await Db.NotificationLogs
            .Where(l => ids.Contains(l.NotificationId) && l.Notification.UserId == userId
                        && l.Channel == NotificationChannel.Browser && l.Status == NotificationLogStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Status, NotificationLogStatus.Sent)
                .SetProperty(l => l.SentAtUtc, now)
                .SetProperty(l => l.LastAttemptAtUtc, now)
                .SetProperty(l => l.AttemptCount, l => l.AttemptCount + 1), ct);
        return Json(new { ok = true, updated });
    }
}
