using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Enums;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Delivery log of one notification on one channel. Table: dbo.NotificationLogs
/// UNIQUE (NotificationId, Channel) => an email can never be logged/sent twice.
/// </summary>
public class NotificationLog
{
    public long NotificationLogId { get; set; }

    public long NotificationId { get; set; }
    public Notification Notification { get; set; } = null!;

    public NotificationChannel Channel { get; set; }
    public NotificationLogStatus Status { get; set; } = NotificationLogStatus.Pending;

    /// <summary>Email address or browser endpoint label.</summary>
    [StringLength(256)]
    public string? Recipient { get; set; }

    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }

    [StringLength(2000)]
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
