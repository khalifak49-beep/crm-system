using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace CrmSystem.Models.Identity;

/// <summary>
/// System user. Table: dbo.ApplicationUsers
/// Standard Identity columns + CRM columns.
/// </summary>
public class ApplicationUser : IdentityUser
{
    [Required, StringLength(150)]
    public string FullName { get; set; } = string.Empty;

    [StringLength(150)]
    public string? FullNameAr { get; set; }

    [StringLength(100)]
    public string? JobTitle { get; set; }

    /// <summary>Sales Manager responsible for this employee.</summary>
    public string? ManagerId { get; set; }
    public ApplicationUser? Manager { get; set; }
    public ICollection<ApplicationUser> TeamMembers { get; set; } = new List<ApplicationUser>();

    /// <summary>"en" or "ar".</summary>
    [Required, StringLength(5)]
    public string PreferredLanguage { get; set; } = "en";

    [StringLength(400)]
    public string? ProfileImagePath { get; set; }

    public bool IsActive { get; set; } = true;
    public bool ReceiveEmailNotifications { get; set; } = true;
    public bool ReceiveBrowserNotifications { get; set; } = true;

    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Name to show in the UI according to the current language.</summary>
    public string DisplayName(bool arabic) =>
        arabic && !string.IsNullOrWhiteSpace(FullNameAr) ? FullNameAr! : FullName;
}
