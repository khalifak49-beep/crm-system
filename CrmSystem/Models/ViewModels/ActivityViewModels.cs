using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;

namespace CrmSystem.Models.ViewModels;

/// <summary>
/// Follow-up scheduling fields, shared by the activity form ("schedule next contact")
/// and the follow-up completion form ("schedule another follow-up").
/// Validated in the controller only when ScheduleFollowUp is ticked.
/// </summary>
public class NextFollowUpFields
{
    [Display(Name = "Activities.ScheduleFollowUp")]
    public bool ScheduleFollowUp { get; set; }

    [Display(Name = "Field.FollowUpDate")]
    public DateOnly? FollowUpDate { get; set; }

    [Display(Name = "Field.FollowUpTime")]
    public TimeOnly? FollowUpTime { get; set; } = new(9, 0);

    [Display(Name = "Field.FollowUpType")]
    public int? FollowUpTypeId { get; set; }

    [Display(Name = "Field.ContactPerson")]
    public int? FollowUpContactId { get; set; }

    [StringLength(1000, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.FollowUpDescription")]
    public string? FollowUpDescription { get; set; }

    [Display(Name = "Field.AssignedTo")]
    public string? FollowUpAssignedToId { get; set; }

    [Display(Name = "Field.Priority")]
    public FollowUpPriority FollowUpPriority { get; set; } = FollowUpPriority.Medium;

    // display
    public string? FollowUpContactName { get; set; }
    public string? FollowUpAssignedToName { get; set; }
}

public class ActivityFormViewModel : NextFollowUpFields
{
    public int? ActivityId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.Company")]
    public int? ClientId { get; set; }

    [Display(Name = "Field.Contact")]
    public int? ContactId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.ActivityType")]
    public int? ActivityTypeId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.Date")]
    public DateOnly? ActivityDate { get; set; }

    [Display(Name = "Field.Time")]
    public TimeOnly? ActivityTime { get; set; }

    [StringLength(250, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Subject")]
    public string? Subject { get; set; }

    [Display(Name = "Field.Notes")]
    public string? Notes { get; set; }

    [StringLength(500, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Outcome")]
    public string? Outcome { get; set; }

    [Display(Name = "Field.Project")]
    public int? ProjectId { get; set; }

    [Display(Name = "Field.PerformedBy")]
    public string? PerformedById { get; set; }

    public string? RowVersion { get; set; }

    // ----- quick entry: create the company / person from the same form (ClientId / ContactId = 0) -----
    [StringLength(200, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.CompanyName")]
    public string? NewClientName { get; set; }

    [StringLength(150, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.FullName")]
    public string? NewContactName { get; set; }

    [StringLength(100, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.JobTitle")]
    public string? NewContactJobTitle { get; set; }

    [StringLength(30, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Phone")]
    public string? NewContactPhone { get; set; }

    [StringLength(256, ErrorMessage = "Validation.MaxLength")]
    [EmailAddress(ErrorMessage = "Validation.Email")]
    [Display(Name = "Field.Email")]
    public string? NewContactEmail { get; set; }

    public bool IsNewClient => ClientId == 0;
    public bool IsNewContact => ContactId == 0;

    // display
    public string? ClientName { get; set; }
    public string? ContactName { get; set; }
    public string? ProjectName { get; set; }
    public string? PerformedByName { get; set; }
    public bool CanChoosePerformer { get; set; }
    public List<ActivityType> Types { get; set; } = new();

    public static ActivityFormViewModel FromEntity(Activity a) => new()
    {
        ActivityId = a.ActivityId,
        ClientId = a.ClientId,
        ContactId = a.ContactId,
        ActivityTypeId = a.ActivityTypeId,
        ActivityDate = a.ActivityDate,
        ActivityTime = a.ActivityTime,
        Subject = a.Subject,
        Notes = a.Notes,
        Outcome = a.Outcome,
        ProjectId = a.ProjectId,
        PerformedById = a.PerformedById,
        RowVersion = Convert.ToBase64String(a.RowVersion)
    };

    public void ApplyTo(Activity a)
    {
        if (ClientId > 0) a.ClientId = ClientId.Value;
        a.ContactId = ContactId > 0 ? ContactId : null;
        a.ActivityTypeId = ActivityTypeId!.Value;
        a.ActivityDate = ActivityDate!.Value;
        a.ActivityTime = ActivityTime;
        a.Subject = string.IsNullOrWhiteSpace(Subject) ? null : Subject.Trim();
        a.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;   // kept exactly as typed
        a.Outcome = string.IsNullOrWhiteSpace(Outcome) ? null : Outcome.Trim();
        a.ProjectId = ProjectId;
    }
}
