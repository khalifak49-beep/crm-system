using CrmSystem.Models.Enums;

namespace CrmSystem.Models.ReadModels;

/// <summary>
/// Read-only row of view dbo.vw_FollowUpDetails (Follow-ups screen).
/// EffectiveStatus = Overdue as soon as a Pending follow-up passes its due time.
/// </summary>
public class FollowUpDetails
{
    public int FollowUpId { get; set; }
    public int ClientId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyNameAr { get; set; }

    public int? ContactId { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactMobile { get; set; }
    public string? ContactEmail { get; set; }

    public int FollowUpTypeId { get; set; }
    public string FollowUpTypeCode { get; set; } = string.Empty;
    public string FollowUpTypeNameEn { get; set; } = string.Empty;
    public string FollowUpTypeNameAr { get; set; } = string.Empty;
    public string FollowUpTypeIcon { get; set; } = string.Empty;
    public string FollowUpTypeColor { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public TimeOnly DueTime { get; set; }
    public DateTime DueAtUtc { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;

    public FollowUpPriority Priority { get; set; }
    public FollowUpStatus Status { get; set; }
    public FollowUpStatus EffectiveStatus { get; set; }

    public int ReminderMinutesBefore { get; set; }
    public DateTime ReminderAtUtc { get; set; }
    public ReminderStatus ReminderStatus { get; set; }
    public DateTime? ReminderSentAtUtc { get; set; }

    public string AssignedToId { get; set; } = string.Empty;
    public string AssignedToName { get; set; } = string.Empty;
    public string? AssignedToEmail { get; set; }

    public int? SourceActivityId { get; set; }
    public DateOnly? SourceActivityDate { get; set; }
    public string? SourceActivityTypeNameEn { get; set; }
    public string? SourceActivityTypeNameAr { get; set; }
    public string? SourceActivityNotes { get; set; }
    public string? SourceActivityOutcome { get; set; }

    public int? ParentFollowUpId { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public int? QuotationId { get; set; }
    public int? TenderId { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedById { get; set; }
    public int? CompletedActivityId { get; set; }
    public string? CompletionNotes { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
