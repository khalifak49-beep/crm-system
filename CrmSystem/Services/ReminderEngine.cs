using CrmSystem.Data;
using CrmSystem.Infrastructure;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;
using CrmSystem.Models.Identity;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Services;

/// <summary>
/// Background reminder engine (ASP.NET Core BackgroundService).
/// Every scan (Reminders.ScanIntervalSeconds, default 60 s, or immediately when woken):
///   1. Pending follow-ups past their due time      -> Status = Overdue (+ one "overdue" notification)
///   2. Follow-ups whose ReminderAtUtc has arrived  -> reminder notification (24 h before by default)
///        - created less than 24 h before due       -> "late" reminder sent immediately (ReminderStatus = SentLate)
///        - completed / cancelled                   -> never selected (Status filter)
///        - rescheduled                             -> DueAtUtc changed -> new DedupKey -> new reminder allowed
///   3. Quotations close to expiry / tenders close to closing -> notifications; expired ones marked Expired
///   4. Pending / failed e-mail logs                -> sent through SMTP (with retries)
/// All state lives in SQL Server, so nothing is lost when IIS restarts the site.
/// </summary>
public sealed class ReminderEngine : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReminderSignal _signal;
    private readonly ILogger<ReminderEngine> _logger;

    public ReminderEngine(IServiceScopeFactory scopeFactory, ReminderSignal signal, ILogger<ReminderEngine> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Reminder engine started.");
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(60);
            try
            {
                interval = await RunOnceAsync(stoppingToken);
                LastSuccessUtc = DateTime.UtcNow;
                LastError = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reminder engine scan failed; will retry.");
                LastError = ex.GetBaseException().Message;
            }

            try { await _signal.WaitAsync(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
        _logger.LogInformation("Reminder engine stopped.");
    }

    /// <summary>Last successful scan (UTC) - shown by the /health endpoint.</summary>
    public DateTime? LastSuccessUtc { get; private set; }

    /// <summary>Last scan error, null when the last scan succeeded.</summary>
    public string? LastError { get; private set; }

    /// <summary>One full scan. Public so it can be triggered from tests.</summary>
    public async Task<TimeSpan> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        db.AuditEnabled = false; // system updates; notifications are their own log
        var settingsProvider = sp.GetRequiredService<IAppSettings>();
        settingsProvider.Invalidate();
        var settings = await settingsProvider.GetAsync(ct);
        var notifications = sp.GetRequiredService<NotificationService>();
        var email = sp.GetRequiredService<EmailSender>();

        await MarkOverdueAsync(db, notifications, settings, ct);
        await SendFollowUpRemindersAsync(db, notifications, settings, ct);
        await QuotationAndTenderAlertsAsync(db, notifications, settings, ct);
        await DispatchEmailsAsync(db, email, notifications, settings, ct);

        return TimeSpan.FromSeconds(Math.Clamp(settings.GetInt(SettingKeys.ScanIntervalSeconds, 60), 15, 3600));
    }

    // ------------------------------------------------------------------ 1. overdue
    private async Task MarkOverdueAsync(ApplicationDbContext db, NotificationService notifications, SettingsSnapshot settings, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var overdue = await db.FollowUps
            .Include(f => f.Client).Include(f => f.AssignedTo)
            .Where(f => f.Status == FollowUpStatus.Pending && f.DueAtUtc < now)
            .OrderBy(f => f.DueAtUtc).Take(200).ToListAsync(ct);

        foreach (var f in overdue)
        {
            var ar = NotificationService.IsArabic(f.AssignedTo);
            var (title, message) = NotificationService.FollowUpOverdue(f, ClientName(f.Client, ar), ar);
            await notifications.CreateAsync(db, f.AssignedTo, NotificationType.FollowUpOverdue, title, message,
                notifications.Link($"FollowUps?tab=overdue&open={f.FollowUpId}"),
                $"FU-OVERDUE:{f.FollowUpId}:{f.DueAtUtc:yyyyMMddHHmm}:{f.AssignedToId}", settings,
                clientId: f.ClientId, followUpId: f.FollowUpId, eventAtLocal: f.DueDate.ToDateTime(f.DueTime), ct: ct);

            f.Status = FollowUpStatus.Overdue;
        }
        if (overdue.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("{Count} follow-up(s) marked overdue.", overdue.Count);
        }
    }

    // ------------------------------------------------------------------ 2. reminders
    private async Task SendFollowUpRemindersAsync(ApplicationDbContext db, NotificationService notifications, SettingsSnapshot settings, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var due = await db.FollowUps
            .Include(f => f.Client).Include(f => f.Contact).Include(f => f.AssignedTo)
            .Where(f => f.ReminderStatus == ReminderStatus.Scheduled
                        && (f.Status == FollowUpStatus.Pending || f.Status == FollowUpStatus.Overdue)
                        && f.ReminderAtUtc <= now)
            .OrderBy(f => f.ReminderAtUtc).Take(200).ToListAsync(ct);

        foreach (var f in due)
        {
            try
            {
                // "late" = the follow-up was created (or rescheduled) after its reminder time had already passed
                var late = (f.UpdatedAt ?? f.CreatedAt) > f.ReminderAtUtc.AddMinutes(1);
                if (late && !settings.GetBool(SettingKeys.SendLateReminderImmediately, true))
                {
                    // Setting off: no immediate reminder (the user was warned on screen when saving).
                    f.ReminderStatus = ReminderStatus.Skipped;
                    await db.SaveChangesAsync(ct);
                    continue;
                }
                var ar = NotificationService.IsArabic(f.AssignedTo);
                var (title, message) = NotificationService.FollowUpReminder(f, ClientName(f.Client, ar), f.Contact?.FullName, settings.TodayLocal, late, ar);

                await notifications.CreateAsync(db, f.AssignedTo,
                    late ? NotificationType.FollowUpLateReminder : NotificationType.FollowUpReminder,
                    title, message, notifications.Link($"FollowUps?tab=upcoming&open={f.FollowUpId}"),
                    Notification.FollowUpReminderKey(f.FollowUpId, f.DueAtUtc, f.AssignedToId), settings,
                    clientId: f.ClientId, followUpId: f.FollowUpId, eventAtLocal: f.DueDate.ToDateTime(f.DueTime), ct: ct);

                // Whether just created or already existing (duplicate prevented), the reminder is done.
                f.ReminderStatus = late ? ReminderStatus.SentLate : ReminderStatus.Sent;
                f.ReminderSentAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("Reminder sent for follow-up {Id} to {User} (late: {Late}).", f.FollowUpId, f.AssignedTo.UserName, late);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The follow-up was edited at the same moment; the next scan re-evaluates it.
                db.ChangeTracker.Clear();
            }
        }
    }

    // ------------------------------------------------------------------ 3. quotations & tenders
    private async Task QuotationAndTenderAlertsAsync(ApplicationDbContext db, NotificationService notifications, SettingsSnapshot settings, CancellationToken ct)
    {
        var today = settings.TodayLocal;
        var nowLocal = settings.NowLocal;

        // Auto-expire
        await db.Quotations
            .Where(q => (q.Status == QuotationStatus.Sent || q.Status == QuotationStatus.UnderReview) && q.ValidUntil != null && q.ValidUntil < today)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QuotationStatus.Expired).SetProperty(q => q.UpdatedAt, DateTime.UtcNow), ct);
        await db.Tenders
            .Where(t => (t.Status == TenderStatus.Open || t.Status == TenderStatus.Preparing) && t.ClosingDate != null && t.ClosingDate < nowLocal)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TenderStatus.Expired).SetProperty(t => t.UpdatedAt, DateTime.UtcNow), ct);

        var qDays = Math.Clamp(settings.GetInt(SettingKeys.QuotationExpiryDaysBefore, 3), 0, 60);
        var limit = today.AddDays(qDays);
        var quotations = await db.Quotations.Include(q => q.Client)
            .Where(q => (q.Status == QuotationStatus.Sent || q.Status == QuotationStatus.UnderReview)
                        && q.ValidUntil != null && q.ValidUntil >= today && q.ValidUntil <= limit)
            .Take(200).ToListAsync(ct);
        foreach (var q in quotations)
        {
            var recipient = await RecipientAsync(db, q.AssignedToId ?? q.CreatedBy, ct);
            if (recipient == null) continue;
            var ar = NotificationService.IsArabic(recipient);
            var (title, message) = NotificationService.QuotationExpiry(q, ClientName(q.Client, ar), today, ar);
            await notifications.CreateAsync(db, recipient, NotificationType.QuotationExpiry, title, message,
                notifications.Link($"Quotations?open={q.QuotationId}"),
                Notification.QuotationExpiryKey(q.QuotationId, q.ValidUntil!.Value, recipient.Id), settings,
                clientId: q.ClientId, quotationId: q.QuotationId, eventAtLocal: q.ValidUntil.Value.ToDateTime(TimeOnly.MinValue), ct: ct);
        }

        var tDays = Math.Clamp(settings.GetInt(SettingKeys.TenderClosingDaysBefore, 3), 0, 60);
        var tLimit = nowLocal.AddDays(tDays);
        var tenders = await db.Tenders.Include(t => t.Client)
            .Where(t => (t.Status == TenderStatus.Open || t.Status == TenderStatus.Preparing || t.Status == TenderStatus.Submitted)
                        && t.ClosingDate != null && t.ClosingDate >= nowLocal && t.ClosingDate <= tLimit)
            .Take(200).ToListAsync(ct);
        foreach (var t in tenders)
        {
            var recipient = await RecipientAsync(db, t.AssignedToId ?? t.CreatedBy, ct);
            if (recipient == null) continue;
            var ar = NotificationService.IsArabic(recipient);
            var (title, message) = NotificationService.TenderClosing(t, ClientName(t.Client, ar), ar);
            await notifications.CreateAsync(db, recipient, NotificationType.TenderClosing, title, message,
                notifications.Link($"Quotations?tab=tenders&open={t.TenderId}"),
                Notification.TenderClosingKey(t.TenderId, t.ClosingDate!.Value, recipient.Id), settings,
                clientId: t.ClientId, tenderId: t.TenderId, eventAtLocal: t.ClosingDate, ct: ct);
        }
    }

    // ------------------------------------------------------------------ 4. e-mail dispatch
    private async Task DispatchEmailsAsync(ApplicationDbContext db, EmailSender email, NotificationService notifications, SettingsSnapshot settings, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var maxAttempts = Math.Clamp(settings.GetInt(SettingKeys.MaxEmailAttempts, 3), 1, 10);

        var candidates = await db.NotificationLogs.AsNoTracking()
            .Where(l => l.Channel == NotificationChannel.Email
                        && (l.Status == NotificationLogStatus.Pending
                            || (l.Status == NotificationLogStatus.Failed && l.AttemptCount < maxAttempts)))
            .OrderBy(l => l.NotificationLogId).Take(50)
            .Select(l => new { l.NotificationLogId, l.AttemptCount, l.LastAttemptAtUtc, l.Status })
            .ToListAsync(ct);
        // back-off: retry a failed e-mail after 5, 10, 15... minutes
        candidates = candidates.Where(c => c.Status == NotificationLogStatus.Pending
                                           || c.LastAttemptAtUtc == null
                                           || c.LastAttemptAtUtc < now.AddMinutes(-5 * c.AttemptCount)).ToList();
        if (candidates.Count == 0) return;

        SmtpOptions? smtp = null;
        foreach (var c in candidates)
        {
            // Claim the row atomically so two app instances never send the same e-mail
            var claimed = await db.NotificationLogs
                .Where(l => l.NotificationLogId == c.NotificationLogId && l.AttemptCount == c.AttemptCount
                            && (l.Status == NotificationLogStatus.Pending || l.Status == NotificationLogStatus.Failed))
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.AttemptCount, c.AttemptCount + 1).SetProperty(l => l.LastAttemptAtUtc, now), ct);
            if (claimed == 0) continue;

            var log = await db.NotificationLogs.Include(l => l.Notification).ThenInclude(n => n.User)
                .FirstAsync(l => l.NotificationLogId == c.NotificationLogId, ct);

            if (!settings.EmailEnabled)
            {
                Finish(log, NotificationLogStatus.Skipped, "Email channel disabled in settings.");
            }
            else if (string.IsNullOrWhiteSpace(log.Recipient))
            {
                Finish(log, NotificationLogStatus.Skipped, "No recipient e-mail address.");
            }
            else
            {
                smtp ??= await email.LoadOptionsAsync(db, ct);
                if (!smtp.IsConfigured)
                {
                    Finish(log, NotificationLogStatus.Failed, "SMTP is not configured (Settings > Email).");
                }
                else
                {
                    try
                    {
                        var n = log.Notification;
                        var ar = NotificationService.IsArabic(n.User);
                        var link = n.Url == null ? null : notifications.AbsoluteLink(n.Url);
                        var html = EmailSender.BuildHtml(n.Title, n.Message, link, ar ? "فتح في النظام" : "Open in CRM", ar, settings.CompanyDisplayName(ar));
                        await email.SendAsync(smtp, log.Recipient!, n.Title, n.Title + "\n\n" + n.Message + (link == null ? "" : "\n\n" + link), html, ct);
                        Finish(log, NotificationLogStatus.Sent, null);
                        log.SentAtUtc = DateTime.UtcNow;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "E-mail to {To} failed (attempt {Attempt}).", log.Recipient, log.AttemptCount);
                        Finish(log, NotificationLogStatus.Failed, ex.Message);
                    }
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private static void Finish(NotificationLog log, NotificationLogStatus status, string? error)
    {
        log.Status = status;
        log.ErrorMessage = error == null ? null : (error.Length > 2000 ? error[..2000] : error);
    }

    private static async Task<ApplicationUser?> RecipientAsync(ApplicationDbContext db, string? userId, CancellationToken ct) =>
        string.IsNullOrEmpty(userId) ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);

    private static string ClientName(Client? c, bool ar) =>
        c == null ? "?" : ar && !string.IsNullOrWhiteSpace(c.CompanyNameAr) ? c.CompanyNameAr! : c.CompanyName;
}
