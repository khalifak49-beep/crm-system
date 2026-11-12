using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Enums;
using CrmSystem.Models.Identity;

namespace CrmSystem.Models.Entities;

/// <summary>
/// In-app notification (bell) for one user. Table: dbo.Notifications
/// DedupKey is UNIQUE in the database => the same reminder can never be created twice.
/// </summary>
public class Notification
{
    public long NotificationId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public NotificationType NotificationType { get; set; }

    [Required, StringLength(250)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(2000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Link opened by the "details" button.</summary>
    [StringLength(500)]
    public string? Url { get; set; }

    public int? ClientId { get; set; }
    public Client? Client { get; set; }

    public int? FollowUpId { get; set; }
    public FollowUp? FollowUp { get; set; }

    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    public int? TenderId { get; set; }
    public Tender? Tender { get; set; }

    /// <summary>Scheduled date/time of the event (local), shown in the list.</summary>
    public DateTime? EventAtLocal { get; set; }

    [Required, StringLength(200)]
    public string DedupKey { get; set; } = string.Empty;

    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<NotificationLog> Logs { get; set; } = new List<NotificationLog>();

    // ----- DedupKey builders (single source of truth for the key format) -----

    public static string FollowUpReminderKey(int followUpId, DateTime dueAtUtc, string userId) =>
        $"FU-REMIND:{followUpId}:{dueAtUtc:yyyyMMddHHmm}:{userId}";

    public static string QuotationExpiryKey(int quotationId, DateOnly validUntil, string userId) =>
        $"QT-EXPIRY:{quotationId}:{validUntil:yyyyMMdd}:{userId}";

    public static string TenderClosingKey(int tenderId, DateTime closingDate, string userId) =>
        $"TD-CLOSE:{tenderId}:{closingDate:yyyyMMddHHmm}:{userId}";

    public void MarkAsRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAtUtc = DateTime.UtcNow;
    }
}
