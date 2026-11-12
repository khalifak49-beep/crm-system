using System.ComponentModel.DataAnnotations;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Audit trail row. Table: dbo.AuditLogs
/// Filled automatically by ApplicationDbContext for every change of an
/// IAuditable entity, and manually for Login / Export / Import / Settings.
/// </summary>
public class AuditLog
{
    public long AuditLogId { get; set; }

    public string? UserId { get; set; }

    [StringLength(256)]
    public string? UserName { get; set; }

    /// <summary>See AuditActions.</summary>
    [Required, StringLength(50)]
    public string Action { get; set; } = string.Empty;

    [StringLength(100)]
    public string? EntityName { get; set; }

    [StringLength(100)]
    public string? EntityId { get; set; }

    /// <summary>JSON.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON.</summary>
    public string? NewValues { get; set; }

    [StringLength(45)]
    public string? IpAddress { get; set; }

    [StringLength(400)]
    public string? UserAgent { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class AuditActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string SoftDelete = "SoftDelete";
    public const string Restore = "Restore";
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string Export = "Export";
    public const string Import = "Import";
    public const string SettingsChanged = "SettingsChanged";
    public const string RoleChanged = "RoleChanged";
    public const string PasswordChanged = "PasswordChanged";
}
