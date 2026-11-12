using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;

namespace CrmSystem.Models.ViewModels;

public class ProjectFormViewModel : IValidatableObject
{
    public int? ProjectId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.Company")]
    public int? ClientId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(200, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.ProjectName")]
    public string ProjectName { get; set; } = string.Empty;

    [Display(Name = "Field.Description")]
    public string? ProjectDescription { get; set; }

    [Range(0, 999999999999999.999, ErrorMessage = "Validation.Range")]
    [Display(Name = "Field.Value")]
    public decimal? ProjectValue { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Validation.Length")]
    [Display(Name = "Field.Currency")]
    public string Currency { get; set; } = "OMR";

    [Display(Name = "Field.ExpectedStartDate")]
    public DateOnly? ExpectedStartDate { get; set; }

    [Display(Name = "Field.ExpectedEndDate")]
    public DateOnly? ExpectedEndDate { get; set; }

    [Display(Name = "Field.Status")]
    public ProjectStatus ProjectStatus { get; set; } = ProjectStatus.New;

    [Range(0, 100, ErrorMessage = "Validation.Range")]
    [Display(Name = "Field.Probability")]
    public byte Probability { get; set; } = 10;

    [Display(Name = "Field.AssignedTo")]
    public string? AssignedToId { get; set; }

    [Display(Name = "Field.ClosedDate")]
    public DateOnly? ClosedDate { get; set; }

    [StringLength(500, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.LostReason")]
    public string? LostReason { get; set; }

    public string? RowVersion { get; set; }

    // display
    public string? ClientName { get; set; }
    public string? AssignedToName { get; set; }
    public bool CanAssign { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (ExpectedStartDate.HasValue && ExpectedEndDate.HasValue && ExpectedEndDate < ExpectedStartDate)
            yield return new ValidationResult("Validation.EndBeforeStart", new[] { nameof(ExpectedEndDate) });
    }

    public static ProjectFormViewModel FromEntity(Project p) => new()
    {
        ProjectId = p.ProjectId,
        ClientId = p.ClientId,
        ProjectName = p.ProjectName,
        ProjectDescription = p.ProjectDescription,
        ProjectValue = p.ProjectValue,
        Currency = p.Currency,
        ExpectedStartDate = p.ExpectedStartDate,
        ExpectedEndDate = p.ExpectedEndDate,
        ProjectStatus = p.ProjectStatus,
        Probability = p.Probability,
        AssignedToId = p.AssignedToId,
        ClosedDate = p.ClosedDate,
        LostReason = p.LostReason,
        RowVersion = Convert.ToBase64String(p.RowVersion)
    };

    public void ApplyTo(Project p, DateOnly today)
    {
        p.ClientId = ClientId!.Value;
        p.ProjectName = ProjectName.Trim();
        p.ProjectDescription = string.IsNullOrWhiteSpace(ProjectDescription) ? null : ProjectDescription.Trim();
        p.ProjectValue = ProjectValue;
        p.Currency = Currency.Trim().ToUpperInvariant();
        p.ExpectedStartDate = ExpectedStartDate;
        p.ExpectedEndDate = ExpectedEndDate;
        p.ProjectStatus = ProjectStatus;
        p.Probability = ProjectStatus switch { ProjectStatus.Won => 100, ProjectStatus.Lost => 0, _ => Probability };
        // Won / Lost need a closing date (used by dashboard & reports)
        p.ClosedDate = ProjectStatus is ProjectStatus.Won or ProjectStatus.Lost ? (ClosedDate ?? p.ClosedDate ?? today) : null;
        p.LostReason = ProjectStatus == ProjectStatus.Lost && !string.IsNullOrWhiteSpace(LostReason) ? LostReason.Trim() : null;
    }
}

public class ProjectNoteViewModel
{
    public int ProjectId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.Note")]
    public string NoteText { get; set; } = string.Empty;
}

public class ProjectDetailsViewModel
{
    public Project Project { get; set; } = null!;
    public string ClientName { get; set; } = "";
    public string? AssignedToName { get; set; }
    public List<(ProjectNote Note, string? Author)> Notes { get; set; } = new();
    public bool CanEdit { get; set; }
}
