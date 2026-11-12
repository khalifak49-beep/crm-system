using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;

namespace CrmSystem.Models.ViewModels;

/// <summary>Create / reschedule a follow-up.</summary>
public class FollowUpFormViewModel
{
    public int? FollowUpId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.Company")]
    public int? ClientId { get; set; }

    [Display(Name = "Field.ContactPerson")]
    public int? ContactId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.FollowUpType")]
    public int? FollowUpTypeId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.FollowUpDate")]
    public DateOnly? DueDate { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.FollowUpTime")]
    public TimeOnly? DueTime { get; set; } = new(9, 0);

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(1000, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.FollowUpDescription")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Field.AssignedTo")]
    public string? AssignedToId { get; set; }

    [Display(Name = "Field.Priority")]
    public FollowUpPriority Priority { get; set; } = FollowUpPriority.Medium;

    [Display(Name = "Field.Project")]
    public int? ProjectId { get; set; }

    [Display(Name = "Field.Quotation")]
    public int? QuotationId { get; set; }

    [Display(Name = "Field.Tender")]
    public int? TenderId { get; set; }

    [Range(0, 43200, ErrorMessage = "Validation.Range")]
    [Display(Name = "Field.ReminderMinutes")]
    public int ReminderMinutesBefore { get; set; } = FollowUp.DefaultReminderMinutes;

    public int? SourceActivityId { get; set; }
    public string? RowVersion { get; set; }

    // display
    public string? ClientName { get; set; }
    public string? ContactName { get; set; }
    public string? AssignedToName { get; set; }
    public string? ProjectName { get; set; }
    public string? QuotationName { get; set; }
    public string? TenderName { get; set; }
    public bool CanAssign { get; set; }
    public List<ActivityType> Types { get; set; } = new();

    public static FollowUpFormViewModel FromEntity(FollowUp f) => new()
    {
        FollowUpId = f.FollowUpId,
        ClientId = f.ClientId,
        ContactId = f.ContactId,
        FollowUpTypeId = f.FollowUpTypeId,
        DueDate = f.DueDate,
        DueTime = f.DueTime,
        Description = f.Description,
        AssignedToId = f.AssignedToId,
        Priority = f.Priority,
        ProjectId = f.ProjectId,
        QuotationId = f.QuotationId,
        TenderId = f.TenderId,
        ReminderMinutesBefore = f.ReminderMinutesBefore,
        SourceActivityId = f.SourceActivityId,
        RowVersion = Convert.ToBase64String(f.RowVersion)
    };
}

/// <summary>"Mark as completed" form: optional activity + optional next follow-up.</summary>
public class CompleteFollowUpViewModel : NextFollowUpFields
{
    public int FollowUpId { get; set; }
    public int ClientId { get; set; }

    [StringLength(1000, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.CompletionNotes")]
    public string? CompletionNotes { get; set; }

    [Display(Name = "FollowUps.LogActivity")]
    public bool LogActivity { get; set; } = true;

    [Display(Name = "Field.ActivityType")]
    public int? ActivityTypeId { get; set; }

    [Display(Name = "Field.Date")]
    public DateOnly? ActivityDate { get; set; }

    [Display(Name = "Field.Time")]
    public TimeOnly? ActivityTime { get; set; }

    [Display(Name = "Field.Notes")]
    public string? ActivityNotes { get; set; }

    [StringLength(500, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Outcome")]
    public string? ActivityOutcome { get; set; }

    public string? RowVersion { get; set; }

    // display
    public string? ClientName { get; set; }
    public string? Description { get; set; }
    public List<ActivityType> Types { get; set; } = new();
}

public class CancelFollowUpViewModel
{
    public int FollowUpId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(500, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.CancelReason")]
    public string CancelReason { get; set; } = string.Empty;

    public string? RowVersion { get; set; }

    // display
    public string? ClientName { get; set; }
    public string? Description { get; set; }
}

/// <summary>Details popup.</summary>
public class FollowUpDetailsViewModel
{
    public ReadModels.FollowUpDetails Item { get; set; } = null!;
    public Client Client { get; set; } = null!;
    public string? PrimaryContact { get; set; }
    public List<ReadModels.ActivityDetails> RecentActivities { get; set; } = new();
    public List<(int Id, DateOnly Date, string Description, FollowUpStatus Status)> Chain { get; set; } = new();
    public bool CanEdit { get; set; }
    public bool IsOpen => Item.Status is FollowUpStatus.Pending or FollowUpStatus.Overdue;
}
