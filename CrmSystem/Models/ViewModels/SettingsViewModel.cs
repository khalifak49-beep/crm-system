using System.ComponentModel.DataAnnotations;

namespace CrmSystem.Models.ViewModels;

/// <summary>Settings screen (Admin). Values are stored in dbo.SystemSettings.</summary>
public class SettingsViewModel
{
    // Company
    [Required(ErrorMessage = "Validation.Required"), StringLength(200, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Settings.CompanyName")]
    public string CompanyName { get; set; } = "";

    [StringLength(200, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Settings.CompanyNameAr")]
    public string? CompanyNameAr { get; set; }

    public string? LogoPath { get; set; }

    // General
    [Display(Name = "Settings.DefaultLanguage")]
    public string DefaultLanguage { get; set; } = "en";

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Settings.TimeZone")]
    public string TimeZone { get; set; } = "Asia/Muscat";

    [Required(ErrorMessage = "Validation.Required"), StringLength(3, MinimumLength = 3, ErrorMessage = "Validation.Length")]
    [Display(Name = "Field.Currency")]
    public string Currency { get; set; } = "OMR";

    // SMTP
    [StringLength(200, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Settings.SmtpHost")]
    public string? SmtpHost { get; set; }

    [Range(1, 65535, ErrorMessage = "Validation.Range")]
    [Display(Name = "Settings.SmtpPort")]
    public int SmtpPort { get; set; } = 587;

    [Display(Name = "Settings.SmtpSsl")]
    public bool SmtpEnableSsl { get; set; } = true;

    [StringLength(200, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Settings.SmtpUser")]
    public string? SmtpUsername { get; set; }

    /// <summary>Write-only: empty = keep the stored (encrypted) password.</summary>
    [DataType(DataType.Password)]
    [Display(Name = "Settings.SmtpPassword")]
    public string? SmtpPassword { get; set; }

    public bool ClearSmtpPassword { get; set; }
    public bool HasSmtpPassword { get; set; }

    [EmailAddress(ErrorMessage = "Validation.Email"), StringLength(256, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Settings.SmtpFrom")]
    public string? SmtpFromEmail { get; set; }

    [StringLength(100, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Settings.SmtpFromName")]
    public string? SmtpFromName { get; set; }

    // Notifications
    [Display(Name = "Settings.InApp")]
    public bool InAppEnabled { get; set; } = true;

    [Display(Name = "Settings.Email")]
    public bool EmailEnabled { get; set; }

    [Display(Name = "Settings.Browser")]
    public bool BrowserEnabled { get; set; } = true;

    [Range(10, 600, ErrorMessage = "Validation.Range")]
    [Display(Name = "Settings.PollInterval")]
    public int PollIntervalSeconds { get; set; } = 30;

    [Range(1, 10, ErrorMessage = "Validation.Range")]
    [Display(Name = "Settings.MaxEmailAttempts")]
    public int MaxEmailAttempts { get; set; } = 3;

    // Reminders
    [Range(0, 43200, ErrorMessage = "Validation.Range")]
    [Display(Name = "Settings.ReminderMinutes")]
    public int FollowUpMinutesBefore { get; set; } = 1440;

    [Display(Name = "Settings.LateReminder")]
    public bool SendLateReminderImmediately { get; set; } = true;

    [Range(0, 60, ErrorMessage = "Validation.Range")]
    [Display(Name = "Settings.QuotationDays")]
    public int QuotationExpiryDaysBefore { get; set; } = 3;

    [Range(0, 60, ErrorMessage = "Validation.Range")]
    [Display(Name = "Settings.TenderDays")]
    public int TenderClosingDaysBefore { get; set; } = 3;

    [Range(15, 3600, ErrorMessage = "Validation.Range")]
    [Display(Name = "Settings.ScanInterval")]
    public int ScanIntervalSeconds { get; set; } = 60;

    // Security
    [Display(Name = "Settings.EmployeesAssignedOnly")]
    public bool EmployeesSeeOnlyAssignedClients { get; set; } = true;

    [Range(6, 64, ErrorMessage = "Validation.Range")]
    [Display(Name = "Settings.PasswordMinLength")]
    public int PasswordMinLength { get; set; } = 8;

    [Range(1, 1440, ErrorMessage = "Validation.Range")]
    [Display(Name = "Settings.LockoutMinutes")]
    public int LockoutMinutes { get; set; } = 15;

    [Range(3, 20, ErrorMessage = "Validation.Range")]
    [Display(Name = "Settings.MaxFailedAttempts")]
    public int MaxFailedAccessAttempts { get; set; } = 5;

    public static readonly string[] TimeZones =
    {
        "Asia/Muscat", "Asia/Dubai", "Asia/Riyadh", "Asia/Qatar", "Asia/Kuwait", "Asia/Bahrain",
        "Asia/Baghdad", "Asia/Tehran", "Asia/Karachi", "Asia/Kolkata", "Africa/Cairo", "Europe/London", "UTC"
    };
}
