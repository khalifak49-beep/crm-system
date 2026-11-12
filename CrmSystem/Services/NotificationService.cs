using System.Globalization;
using CrmSystem.Data;
using CrmSystem.Infrastructure;
using CrmSystem.Infrastructure.Localization;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;
using CrmSystem.Models.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Services;

/// <summary>
/// Creates notifications (bell) + one NotificationLogs row per channel:
///   InApp   : Sent immediately (or Skipped if the channel is disabled)
///   Email   : Pending -> sent by the ReminderEngine dispatcher (or Skipped)
///   Browser : Pending -> Sent when the user's browser displays it (or Skipped)
/// Texts are written in the recipient's preferred language (English / Arabic).
/// The UNIQUE DedupKey makes duplicates impossible even with several app instances.
/// </summary>
public sealed class NotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly IConfiguration _config;

    public NotificationService(ILogger<NotificationService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>App-relative link stored in the notification (honours an IIS virtual directory).</summary>
    public string Link(string path)
    {
        var basePath = (_config["App:PathBase"] ?? "/").TrimEnd('/');
        return basePath + "/" + path.TrimStart('/');
    }

    /// <summary>Absolute link for e-mails (App:PublicUrl, e.g. https://crm.company.om).</summary>
    public string? AbsoluteLink(string relative)
    {
        var publicUrl = _config["App:PublicUrl"];
        return string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.TrimEnd('/') + "/" + relative.TrimStart('/');
    }

    /// <summary>
    /// Adds and saves a notification. Returns null when the DedupKey already exists
    /// (= this notification was already created before).
    /// </summary>
    public async Task<Notification?> CreateAsync(ApplicationDbContext db, ApplicationUser recipient, NotificationType type,
        string title, string message, string? url, string dedupKey, SettingsSnapshot settings,
        int? clientId = null, int? followUpId = null, int? projectId = null, int? quotationId = null, int? tenderId = null,
        DateTime? eventAtLocal = null, CancellationToken ct = default)
    {
        if (await db.Notifications.AnyAsync(n => n.DedupKey == dedupKey, ct)) return null;

        var now = DateTime.UtcNow;
        var notification = new Notification
        {
            UserId = recipient.Id,
            NotificationType = type,
            Title = Cut(title, 250),
            Message = Cut(message, 2000),
            Url = url,
            DedupKey = dedupKey,
            ClientId = clientId,
            FollowUpId = followUpId,
            ProjectId = projectId,
            QuotationId = quotationId,
            TenderId = tenderId,
            EventAtLocal = eventAtLocal,
            CreatedAtUtc = now
        };

        notification.Logs.Add(new NotificationLog
        {
            Channel = NotificationChannel.InApp,
            Status = settings.InAppEnabled ? NotificationLogStatus.Sent : NotificationLogStatus.Skipped,
            SentAtUtc = settings.InAppEnabled ? now : null,
            AttemptCount = settings.InAppEnabled ? 1 : 0,
            LastAttemptAtUtc = now,
            ErrorMessage = settings.InAppEnabled ? null : "In-app channel disabled in settings."
        });

        var emailOk = settings.EmailEnabled && recipient.ReceiveEmailNotifications && !string.IsNullOrWhiteSpace(recipient.Email) && recipient.IsActive;
        notification.Logs.Add(new NotificationLog
        {
            Channel = NotificationChannel.Email,
            Status = emailOk ? NotificationLogStatus.Pending : NotificationLogStatus.Skipped,
            Recipient = recipient.Email,
            ErrorMessage = emailOk ? null
                : !settings.EmailEnabled ? "Email channel disabled in settings."
                : !recipient.ReceiveEmailNotifications ? "User turned off email notifications."
                : "User has no email address."
        });

        var browserOk = settings.BrowserEnabled && recipient.ReceiveBrowserNotifications;
        notification.Logs.Add(new NotificationLog
        {
            Channel = NotificationChannel.Browser,
            Status = browserOk ? NotificationLogStatus.Pending : NotificationLogStatus.Skipped,
            ErrorMessage = browserOk ? null : "Browser channel disabled."
        });

        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(ct);
            return notification;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another instance created the same notification at the same moment: keep only one.
            db.Entry(notification).State = EntityState.Detached;
            foreach (var log in notification.Logs) db.Entry(log).State = EntityState.Detached;
            _logger.LogInformation("Notification {Key} already exists - duplicate prevented.", dedupKey);
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Message texts (EN / AR) - same wording as the requirement
    // ------------------------------------------------------------------
    public static bool IsArabic(ApplicationUser user) => user.PreferredLanguage == "ar";

    private static CultureInfo Culture(bool ar) => ar ? AppCultures.ArabicCulture : AppCultures.EnglishCulture;

    public static string FormatDate(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string FormatTime(TimeOnly t, bool ar) => t.ToString("hh:mm tt", Culture(ar));

    /// <summary>"Tomorrow" / "Today" / "On 15/11/2026" relative to the local date now.</summary>
    private static string When(DateOnly due, DateOnly today, bool ar) =>
        due == today ? (ar ? "اليوم" : "Today")
        : due == today.AddDays(1) ? (ar ? "غداً" : "Tomorrow")
        : due < today ? (ar ? $"منذ {FormatDate(due)}" : $"Since {FormatDate(due)}")
        : (ar ? $"بتاريخ {FormatDate(due)}" : $"On {FormatDate(due)}");

    public static (string Title, string Message) FollowUpReminder(FollowUp f, string clientName, string? contactName, DateOnly todayLocal, bool late, bool ar)
    {
        var when = When(f.DueDate, todayLocal, ar);
        string title;
        if (ar)
        {
            title = $"تذكير: {when} يجب عليك التواصل مع {clientName}.";
            if (late) title = "تذكير (فات وقت التذكير المسبق): " + title["تذكير: ".Length..];
        }
        else
        {
            title = $"Reminder: {when} you have to contact {clientName}.";
            if (late) title = "Late reminder: " + title["Reminder: ".Length..];
        }

        var lines = ar
            ? new[]
            {
                $"الشخص المطلوب: {contactName ?? "—"}",
                $"الغرض: {f.Description}",
                $"التاريخ: {FormatDate(f.DueDate)}",
                $"الوقت: {FormatTime(f.DueTime, true)}"
            }
            : new[]
            {
                $"Contact Person: {contactName ?? "—"}",
                $"Purpose: {f.Description}",
                $"Scheduled Date: {FormatDate(f.DueDate)}",
                $"Scheduled Time: {FormatTime(f.DueTime, false)}"
            };
        return (title, string.Join("\n", lines));
    }

    public static (string Title, string Message) FollowUpOverdue(FollowUp f, string clientName, bool ar) => ar
        ? ($"متابعة متأخرة: {clientName}", $"الغرض: {f.Description}\nكان الموعد: {FormatDate(f.DueDate)} {FormatTime(f.DueTime, true)}")
        : ($"Overdue follow-up: {clientName}", $"Purpose: {f.Description}\nWas due: {FormatDate(f.DueDate)} {FormatTime(f.DueTime, false)}");

    public static (string Title, string Message) QuotationExpiry(Quotation q, string clientName, DateOnly today, bool ar)
    {
        var days = q.ValidUntil!.Value.DayNumber - today.DayNumber;
        return ar
            ? ($"عرض السعر {q.QuotationNumber} ينتهي خلال {days} يوم", $"الشركة: {clientName}\nتاريخ الانتهاء: {FormatDate(q.ValidUntil.Value)}\nالقيمة: {q.QuotationValue?.ToString("N3", CultureInfo.InvariantCulture) ?? "—"} {q.Currency}")
            : ($"Quotation {q.QuotationNumber} expires in {days} day(s)", $"Company: {clientName}\nValid until: {FormatDate(q.ValidUntil.Value)}\nValue: {q.QuotationValue?.ToString("N3", CultureInfo.InvariantCulture) ?? "—"} {q.Currency}");
    }

    public static (string Title, string Message) TenderClosing(Tender t, string clientName, bool ar)
    {
        var closing = t.ClosingDate!.Value;
        var date = FormatDate(DateOnly.FromDateTime(closing)) + " " + FormatTime(TimeOnly.FromDateTime(closing), ar);
        return ar
            ? ($"المناقصة {t.TenderNumber} تُغلق قريباً", $"الشركة: {clientName}\nالعنوان: {t.Title}\nموعد الإغلاق: {date}")
            : ($"Tender {t.TenderNumber} is closing soon", $"Company: {clientName}\nTitle: {t.Title}\nClosing: {date}");
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
