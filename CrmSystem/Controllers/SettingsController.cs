using System.Globalization;
using CrmSystem.Infrastructure;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Identity;
using CrmSystem.Models.ViewModels;
using CrmSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// System settings (Admin only). Secrets (SMTP password) are encrypted with Data Protection
/// and never sent back to the browser. Every change is written to AuditLogs (secrets masked).
/// </summary>
[Authorize(Policy = AppPolicies.AdminOnly)]
public class SettingsController : AppController
{
    private readonly IAppSettings _settings;
    private readonly SecretProtector _secrets;
    private readonly EmailSender _email;
    private readonly IWebHostEnvironment _env;
    private readonly ReminderSignal _signal;

    public SettingsController(IAppSettings settings, SecretProtector secrets, EmailSender email, IWebHostEnvironment env, ReminderSignal signal)
    {
        _settings = settings;
        _secrets = secrets;
        _email = email;
        _env = env;
        _signal = signal;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var values = await Db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, StringComparer.OrdinalIgnoreCase, ct);
        string S(string key, string def = "") => values.TryGetValue(key, out var v) && v != null ? v : def;
        int I(string key, int def) => int.TryParse(S(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
        bool B(string key, bool def) => bool.TryParse(S(key), out var v) ? v : def;

        var model = new SettingsViewModel
        {
            CompanyName = S(SettingKeys.CompanyName, "My Company"),
            CompanyNameAr = S(SettingKeys.CompanyNameAr),
            LogoPath = S(SettingKeys.CompanyLogoPath),
            DefaultLanguage = S(SettingKeys.DefaultLanguage, "en"),
            TimeZone = S(SettingKeys.TimeZone, "Asia/Muscat"),
            Currency = S(SettingKeys.Currency, "OMR"),
            SmtpHost = S(SettingKeys.SmtpHost),
            SmtpPort = I(SettingKeys.SmtpPort, 587),
            SmtpEnableSsl = B(SettingKeys.SmtpEnableSsl, true),
            SmtpUsername = S(SettingKeys.SmtpUsername),
            HasSmtpPassword = !string.IsNullOrEmpty(S(SettingKeys.SmtpPassword)),
            SmtpFromEmail = S(SettingKeys.SmtpFromEmail),
            SmtpFromName = S(SettingKeys.SmtpFromName, "CRM System"),
            InAppEnabled = B(SettingKeys.InAppEnabled, true),
            EmailEnabled = B(SettingKeys.EmailEnabled, false),
            BrowserEnabled = B(SettingKeys.BrowserEnabled, true),
            PollIntervalSeconds = I(SettingKeys.PollIntervalSeconds, 30),
            MaxEmailAttempts = I(SettingKeys.MaxEmailAttempts, 3),
            FollowUpMinutesBefore = I(SettingKeys.FollowUpMinutesBefore, 1440),
            SendLateReminderImmediately = B(SettingKeys.SendLateReminderImmediately, true),
            QuotationExpiryDaysBefore = I(SettingKeys.QuotationExpiryDaysBefore, 3),
            TenderClosingDaysBefore = I(SettingKeys.TenderClosingDaysBefore, 3),
            ScanIntervalSeconds = I(SettingKeys.ScanIntervalSeconds, 60),
            EmployeesSeeOnlyAssignedClients = B(SettingKeys.EmployeesSeeOnlyAssignedClients, true),
            PasswordMinLength = I(SettingKeys.PasswordMinLength, 8),
            LockoutMinutes = I(SettingKeys.LockoutMinutes, 15),
            MaxFailedAccessAttempts = I(SettingKeys.MaxFailedAccessAttempts, 5)
        };
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Save(SettingsViewModel model, CancellationToken ct)
    {
        if (!SettingsViewModel.TimeZones.Contains(model.TimeZone))
            ModelState.AddModelError(nameof(model.TimeZone), T["Validation.InvalidShort"]);
        else
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(model.TimeZone); }
            catch (Exception) { ModelState.AddModelError(nameof(model.TimeZone), T["Settings.TimeZoneUnknown"]); }
        }
        if (model.EmailEnabled && (string.IsNullOrWhiteSpace(model.SmtpHost) || string.IsNullOrWhiteSpace(model.SmtpFromEmail)))
            ModelState.AddModelError(nameof(model.SmtpHost), T["Settings.SmtpRequired"]);
        if (!ModelState.IsValid)
            return Fail(string.Join(" • ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct()));

        var rows = await Db.SystemSettings.ToDictionaryAsync(s => s.SettingKey, StringComparer.OrdinalIgnoreCase, ct);
        var changed = new Dictionary<string, object?>();
        var old = new Dictionary<string, object?>();

        void Set(string key, string? value)
        {
            if (!rows.TryGetValue(key, out var row)) return;
            value ??= "";
            if ((row.SettingValue ?? "") == value) return;
            old[key] = row.IsEncrypted ? "***" : row.SettingValue;
            changed[key] = row.IsEncrypted ? "***" : value;
            row.SettingValue = value;
            row.UpdatedAt = DateTime.UtcNow;
            row.UpdatedBy = UserId;
        }
        string B(bool v) => v ? "true" : "false";
        string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        Set(SettingKeys.CompanyName, model.CompanyName.Trim());
        Set(SettingKeys.CompanyNameAr, model.CompanyNameAr?.Trim());
        Set(SettingKeys.DefaultLanguage, model.DefaultLanguage == "ar" ? "ar" : "en");
        Set(SettingKeys.TimeZone, model.TimeZone);
        Set(SettingKeys.Currency, model.Currency.Trim().ToUpperInvariant());
        Set(SettingKeys.SmtpHost, model.SmtpHost?.Trim());
        Set(SettingKeys.SmtpPort, I(model.SmtpPort));
        Set(SettingKeys.SmtpEnableSsl, B(model.SmtpEnableSsl));
        Set(SettingKeys.SmtpUsername, model.SmtpUsername?.Trim());
        if (model.ClearSmtpPassword) Set(SettingKeys.SmtpPassword, "");
        else if (!string.IsNullOrEmpty(model.SmtpPassword)) Set(SettingKeys.SmtpPassword, _secrets.Protect(model.SmtpPassword));
        Set(SettingKeys.SmtpFromEmail, model.SmtpFromEmail?.Trim());
        Set(SettingKeys.SmtpFromName, model.SmtpFromName?.Trim());
        Set(SettingKeys.InAppEnabled, B(model.InAppEnabled));
        Set(SettingKeys.EmailEnabled, B(model.EmailEnabled));
        Set(SettingKeys.BrowserEnabled, B(model.BrowserEnabled));
        Set(SettingKeys.PollIntervalSeconds, I(model.PollIntervalSeconds));
        Set(SettingKeys.MaxEmailAttempts, I(model.MaxEmailAttempts));
        Set(SettingKeys.FollowUpMinutesBefore, I(model.FollowUpMinutesBefore));
        Set(SettingKeys.SendLateReminderImmediately, B(model.SendLateReminderImmediately));
        Set(SettingKeys.QuotationExpiryDaysBefore, I(model.QuotationExpiryDaysBefore));
        Set(SettingKeys.TenderClosingDaysBefore, I(model.TenderClosingDaysBefore));
        Set(SettingKeys.ScanIntervalSeconds, I(model.ScanIntervalSeconds));
        Set(SettingKeys.EmployeesSeeOnlyAssignedClients, B(model.EmployeesSeeOnlyAssignedClients));
        Set(SettingKeys.PasswordMinLength, I(model.PasswordMinLength));
        Set(SettingKeys.LockoutMinutes, I(model.LockoutMinutes));
        Set(SettingKeys.MaxFailedAccessAttempts, I(model.MaxFailedAccessAttempts));

        if (changed.Count == 0) return Saved(T["Settings.NoChanges"]);

        Db.AddAudit(AuditActions.SettingsChanged, nameof(SystemSetting), null, changed, old);
        await Db.SaveChangesAsync(ct);
        _settings.Invalidate();
        _signal.Wake();

        var restart = changed.Keys.Any(k => k.StartsWith("Security.Password") || k.StartsWith("Security.Lockout") || k.StartsWith("Security.MaxFailed"));
        return Saved(restart ? T["Settings.SavedRestart"] : T["Settings.Saved"]);
    }

    public sealed class TestEmailRequest { public string? To { get; set; } }

    [HttpPost]
    public async Task<IActionResult> TestEmail([FromBody] TestEmailRequest request, CancellationToken ct)
    {
        var to = string.IsNullOrWhiteSpace(request.To) ? await Db.Users.Where(u => u.Id == UserId).Select(u => u.Email).FirstOrDefaultAsync(ct) : request.To.Trim();
        if (string.IsNullOrWhiteSpace(to) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(to))
            return Fail(T["Validation.Email", T["Field.Email"]]);

        var smtp = await _email.LoadOptionsAsync(Db, ct);
        if (!smtp.IsConfigured) return Fail(T["Settings.SmtpRequired"]);
        var settings = await SettingsAsync();
        try
        {
            var title = IsArabic ? "رسالة تجريبية من نظام إدارة العملاء" : "CRM System test email";
            var body = IsArabic ? "إعدادات البريد تعمل بشكل صحيح." : "Your SMTP settings are working.";
            await _email.SendAsync(smtp, to, title, body, EmailSender.BuildHtml(title, body, null, null, IsArabic, settings.CompanyDisplayName(IsArabic)), ct);
            return Saved(T["Settings.TestSent", to]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(T["Settings.TestFailed", ex.GetBaseException().Message]);
        }
    }

    // ---------------------------------------------------------------- logo
    private static readonly Dictionary<string, byte[][]> ImageSignatures = new()
    {
        [".png"] = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } },
        [".jpg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".webp"] = new[] { new byte[] { 0x52, 0x49, 0x46, 0x46 } }
    };

    [HttpPost]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> UploadLogo(IFormFile? logo, CancellationToken ct)
    {
        if (logo == null || logo.Length == 0) return Fail(T["Import.Error.NoFile"]);
        if (logo.Length > 1024 * 1024) return Fail(T["Settings.LogoTooLarge"]);
        var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
        if (!ImageSignatures.TryGetValue(ext, out var signatures)) return Fail(T["Settings.LogoType"]);

        await using var ms = new MemoryStream();
        await logo.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (!signatures.Any(sig => bytes.Length >= sig.Length && bytes.AsSpan(0, sig.Length).SequenceEqual(sig)))
            return Fail(T["Settings.LogoType"]); // content does not match the extension

        var folder = Path.Combine(_env.WebRootPath, "uploads");
        Directory.CreateDirectory(folder);
        var fileName = $"logo-{Guid.NewGuid():N}{ext}";
        await System.IO.File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes, ct);

        var row = await Db.SystemSettings.FirstAsync(s => s.SettingKey == SettingKeys.CompanyLogoPath, ct);
        DeleteLogoFile(row.SettingValue);
        row.SettingValue = "~/uploads/" + fileName;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = UserId;
        Db.AddAudit(AuditActions.SettingsChanged, nameof(SystemSetting), SettingKeys.CompanyLogoPath, new { Logo = fileName });
        await Db.SaveChangesAsync(ct);
        _settings.Invalidate();
        return Saved(T["Settings.LogoSaved"], new { url = Url.Content(row.SettingValue) });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveLogo(CancellationToken ct)
    {
        var row = await Db.SystemSettings.FirstAsync(s => s.SettingKey == SettingKeys.CompanyLogoPath, ct);
        DeleteLogoFile(row.SettingValue);
        row.SettingValue = "";
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = UserId;
        Db.AddAudit(AuditActions.SettingsChanged, nameof(SystemSetting), SettingKeys.CompanyLogoPath, new { Logo = (string?)null });
        await Db.SaveChangesAsync(ct);
        _settings.Invalidate();
        return Saved(T["Settings.LogoRemoved"]);
    }

    private void DeleteLogoFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("~/uploads/logo-")) return;
        var full = Path.Combine(_env.WebRootPath, "uploads", Path.GetFileName(path));
        if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
    }
}
