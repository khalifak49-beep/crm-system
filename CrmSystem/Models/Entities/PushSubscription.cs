using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Identity;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Browser Web Push subscription of a user. Table: dbo.PushSubscriptions
/// </summary>
public class PushSubscription
{
    public int PushSubscriptionId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    [Required, StringLength(800)]
    public string Endpoint { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string P256dh { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Auth { get; set; } = string.Empty;

    [StringLength(400)]
    public string? UserAgent { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAtUtc { get; set; }
}
