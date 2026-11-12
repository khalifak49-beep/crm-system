using System.ComponentModel.DataAnnotations;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Key/value system setting. Table: dbo.SystemSettings
/// Secrets (DataType = "secret") are stored encrypted with ASP.NET Core Data Protection.
/// </summary>
public class SystemSetting
{
    public int SettingId { get; set; }

    [Required, StringLength(100)]
    public string SettingKey { get; set; } = string.Empty;

    public string? SettingValue { get; set; }

    [Required, StringLength(50)]
    public string GroupName { get; set; } = string.Empty;

    /// <summary>string, int, bool, decimal, json, secret, image, email, timezone, language</summary>
    [Required, StringLength(20)]
    public string DataType { get; set; } = "string";

    public bool IsEncrypted { get; set; }

    [StringLength(300)]
    public string? DescriptionEn { get; set; }

    [StringLength(300)]
    public string? DescriptionAr { get; set; }

    public int SortOrder { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}

/// <summary>Setting keys seeded by Database/05_SeedData.sql.</summary>
public static class SettingKeys
{
    public const string CompanyName = "Company.Name";
    public const string CompanyNameAr = "Company.NameAr";
    public const string CompanyLogoPath = "Company.LogoPath";

    public const string DefaultLanguage = "System.DefaultLanguage";
    public const string TimeZone = "System.TimeZone";
    public const string Currency = "System.Currency";
    public const string DateFormat = "System.DateFormat";

    public const string SmtpHost = "Smtp.Host";
    public const string SmtpPort = "Smtp.Port";
    public const string SmtpEnableSsl = "Smtp.EnableSsl";
    public const string SmtpUsername = "Smtp.Username";
    public const string SmtpPassword = "Smtp.Password";
    public const string SmtpFromEmail = "Smtp.FromEmail";
    public const string SmtpFromName = "Smtp.FromName";

    public const string InAppEnabled = "Notifications.InAppEnabled";
    public const string EmailEnabled = "Notifications.EmailEnabled";
    public const string BrowserEnabled = "Notifications.BrowserEnabled";
    public const string PollIntervalSeconds = "Notifications.PollIntervalSeconds";
    public const string MaxEmailAttempts = "Notifications.MaxEmailAttempts";

    public const string FollowUpMinutesBefore = "Reminders.FollowUpMinutesBefore";
    public const string SendLateReminderImmediately = "Reminders.SendLateReminderImmediately";
    public const string QuotationExpiryDaysBefore = "Reminders.QuotationExpiryDaysBefore";
    public const string TenderClosingDaysBefore = "Reminders.TenderClosingDaysBefore";
    public const string ScanIntervalSeconds = "Reminders.ScanIntervalSeconds";

    public const string EmployeesSeeOnlyAssignedClients = "Security.EmployeesSeeOnlyAssignedClients";
    public const string PasswordMinLength = "Security.PasswordMinLength";
    public const string LockoutMinutes = "Security.LockoutMinutes";
    public const string MaxFailedAccessAttempts = "Security.MaxFailedAccessAttempts";
}
