using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Common;
using CrmSystem.Models.Enums;
using CrmSystem.Models.Identity;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Scheduled next contact + reminder state. Table: dbo.FollowUps
///
/// DueDate/DueTime : LOCAL time (TimeZoneId, default Asia/Muscat).
/// DueAtUtc        : calculated by the application from DueDate + DueTime + TimeZoneId.
/// ReminderAtUtc   : computed by SQL Server = DueAtUtc - ReminderMinutesBefore (read-only).
/// When DueDate/DueTime change, the app recalculates DueAtUtc and resets
/// ReminderStatus to Scheduled - the reminder moves automatically.
/// </summary>
public class FollowUp : IAuditable, ISoftDelete
{
    public const string DefaultTimeZoneId = "Asia/Muscat";
    public const int DefaultReminderMinutes = 1440; // 24 hours

    public int FollowUpId { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    /// <summary>Type of contact to perform (call, email, visit...).</summary>
    public int FollowUpTypeId { get; set; }
    public ActivityType FollowUpType { get; set; } = null!;

    /// <summary>Activity that created this follow-up.</summary>
    public int? SourceActivityId { get; set; }
    public Activity? SourceActivity { get; set; }

    /// <summary>Previous follow-up in the chain.</summary>
    public int? ParentFollowUpId { get; set; }
    public FollowUp? ParentFollowUp { get; set; }
    public ICollection<FollowUp> ChildFollowUps { get; set; } = new List<FollowUp>();

    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    public int? TenderId { get; set; }
    public Tender? Tender { get; set; }

    [Required, StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    public DateOnly DueDate { get; set; }
    public TimeOnly DueTime { get; set; } = new(9, 0);

    [Required, StringLength(64)]
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public DateTime DueAtUtc { get; set; }

    [Required]
    public string AssignedToId { get; set; } = string.Empty;
    public ApplicationUser AssignedTo { get; set; } = null!;

    public FollowUpPriority Priority { get; set; } = FollowUpPriority.Medium;
    public FollowUpStatus Status { get; set; } = FollowUpStatus.Pending;

    [Range(0, 43200)]
    public int ReminderMinutesBefore { get; set; } = DefaultReminderMinutes;

    /// <summary>Computed column (DueAtUtc - ReminderMinutesBefore). Read-only.</summary>
    public DateTime ReminderAtUtc { get; private set; }

    public ReminderStatus ReminderStatus { get; set; } = ReminderStatus.Scheduled;
    public DateTime? ReminderSentAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedById { get; set; }
    public ApplicationUser? CompletedBy { get; set; }

    /// <summary>Activity logged when this follow-up was completed.</summary>
    public int? CompletedActivityId { get; set; }
    public Activity? CompletedActivity { get; set; }

    [StringLength(1000)]
    public string? CompletionNotes { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelledById { get; set; }

    [StringLength(500)]
    public string? CancelReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>True when still open (Pending or Overdue).</summary>
    public bool IsOpen => Status is FollowUpStatus.Pending or FollowUpStatus.Overdue;

    /// <summary>
    /// Converts DueDate + DueTime in TimeZoneId to UTC and stores it in DueAtUtc.
    /// Call every time DueDate, DueTime or TimeZoneId change.
    /// If the due time changed, the reminder is re-armed.
    /// </summary>
    public void SetSchedule(DateOnly dueDate, TimeOnly dueTime, string? timeZoneId = null)
    {
        var tzId = string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneId : timeZoneId;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var local = DateTime.SpecifyKind(dueDate.ToDateTime(dueTime), DateTimeKind.Unspecified);
        var newUtc = TimeZoneInfo.ConvertTimeToUtc(local, tz);

        var changed = newUtc != DueAtUtc;
        DueDate = dueDate;
        DueTime = dueTime;
        TimeZoneId = tzId;
        DueAtUtc = newUtc;

        if (changed && IsOpen)
        {
            ReminderStatus = ReminderStatus.Scheduled;
            ReminderSentAtUtc = null;
        }
    }

    /// <summary>Reminder time computed in memory (same formula as the SQL computed column).</summary>
    public DateTime CalculateReminderAtUtc() => DueAtUtc.AddMinutes(-ReminderMinutesBefore);
}
