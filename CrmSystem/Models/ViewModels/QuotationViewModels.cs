using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;

namespace CrmSystem.Models.ViewModels;

public class QuotationFormViewModel
{
    public int? QuotationId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(50, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.QuotationNumber")]
    public string QuotationNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.Company")]
    public int? ClientId { get; set; }

    [Display(Name = "Field.ContactPerson")]
    public int? ContactId { get; set; }

    [Display(Name = "Field.Project")]
    public int? ProjectId { get; set; }

    [Display(Name = "Field.Tender")]
    public int? TenderId { get; set; }

    [StringLength(250, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Title")]
    public string? Title { get; set; }

    [Display(Name = "Field.SubmissionDate")]
    public DateOnly? SubmissionDate { get; set; }

    [Range(0, 999999999999999.999, ErrorMessage = "Validation.Range")]
    [Display(Name = "Field.Value")]
    public decimal? QuotationValue { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Validation.Length")]
    [Display(Name = "Field.Currency")]
    public string Currency { get; set; } = "OMR";

    [Display(Name = "Field.ValidUntil")]
    public DateOnly? ValidUntil { get; set; }

    [Display(Name = "Field.Status")]
    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;

    [Display(Name = "Field.AssignedTo")]
    public string? AssignedToId { get; set; }

    [Display(Name = "Field.Notes")]
    public string? Notes { get; set; }

    public string? RowVersion { get; set; }

    // display
    public string? ClientName { get; set; }
    public string? ContactName { get; set; }
    public string? ProjectName { get; set; }
    public string? TenderName { get; set; }
    public string? AssignedToName { get; set; }
    public bool CanAssign { get; set; }

    public static QuotationFormViewModel FromEntity(Quotation q) => new()
    {
        QuotationId = q.QuotationId,
        QuotationNumber = q.QuotationNumber,
        ClientId = q.ClientId,
        ContactId = q.ContactId,
        ProjectId = q.ProjectId,
        TenderId = q.TenderId,
        Title = q.Title,
        SubmissionDate = q.SubmissionDate,
        QuotationValue = q.QuotationValue,
        Currency = q.Currency,
        ValidUntil = q.ValidUntil,
        Status = q.Status,
        AssignedToId = q.AssignedToId,
        Notes = q.Notes,
        RowVersion = Convert.ToBase64String(q.RowVersion)
    };

    public void ApplyTo(Quotation q)
    {
        q.QuotationNumber = QuotationNumber.Trim();
        q.ClientId = ClientId!.Value;
        q.ContactId = ContactId;
        q.ProjectId = ProjectId;
        q.TenderId = TenderId;
        q.Title = string.IsNullOrWhiteSpace(Title) ? null : Title.Trim();
        q.SubmissionDate = SubmissionDate;
        q.QuotationValue = QuotationValue;
        q.Currency = Currency.Trim().ToUpperInvariant();
        q.ValidUntil = ValidUntil;
        q.Status = Status;
        q.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
    }
}

public class TenderFormViewModel
{
    public int? TenderId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(50, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.TenderNumber")]
    public string TenderNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.Company")]
    public int? ClientId { get; set; }

    [Display(Name = "Field.Project")]
    public int? ProjectId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(250, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Field.Description")]
    public string? Description { get; set; }

    [Display(Name = "Field.IssueDate")]
    public DateOnly? IssueDate { get; set; }

    [Display(Name = "Field.SubmissionDate")]
    public DateOnly? SubmissionDate { get; set; }

    [Display(Name = "Field.ClosingDate")]
    public DateOnly? ClosingDate { get; set; }

    [Display(Name = "Field.ClosingTime")]
    public TimeOnly? ClosingTime { get; set; } = new(12, 0);

    [Range(0, 999999999999999.999, ErrorMessage = "Validation.Range")]
    [Display(Name = "Field.Value")]
    public decimal? TenderValue { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Validation.Length")]
    [Display(Name = "Field.Currency")]
    public string Currency { get; set; } = "OMR";

    [Display(Name = "Field.Status")]
    public TenderStatus Status { get; set; } = TenderStatus.Open;

    [Display(Name = "Field.AssignedTo")]
    public string? AssignedToId { get; set; }

    [Display(Name = "Field.Notes")]
    public string? Notes { get; set; }

    public string? RowVersion { get; set; }

    // display
    public string? ClientName { get; set; }
    public string? ProjectName { get; set; }
    public string? AssignedToName { get; set; }
    public bool CanAssign { get; set; }

    public static TenderFormViewModel FromEntity(Tender t) => new()
    {
        TenderId = t.TenderId,
        TenderNumber = t.TenderNumber,
        ClientId = t.ClientId,
        ProjectId = t.ProjectId,
        Title = t.Title,
        Description = t.Description,
        IssueDate = t.IssueDate,
        SubmissionDate = t.SubmissionDate,
        ClosingDate = t.ClosingDate.HasValue ? DateOnly.FromDateTime(t.ClosingDate.Value) : null,
        ClosingTime = t.ClosingDate.HasValue ? TimeOnly.FromDateTime(t.ClosingDate.Value) : new TimeOnly(12, 0),
        TenderValue = t.TenderValue,
        Currency = t.Currency,
        Status = t.Status,
        AssignedToId = t.AssignedToId,
        Notes = t.Notes,
        RowVersion = Convert.ToBase64String(t.RowVersion)
    };

    public void ApplyTo(Tender t)
    {
        t.TenderNumber = TenderNumber.Trim();
        t.ClientId = ClientId!.Value;
        t.ProjectId = ProjectId;
        t.Title = Title.Trim();
        t.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        t.IssueDate = IssueDate;
        t.SubmissionDate = SubmissionDate;
        t.ClosingDate = ClosingDate?.ToDateTime(ClosingTime ?? new TimeOnly(12, 0));
        t.TenderValue = TenderValue;
        t.Currency = Currency.Trim().ToUpperInvariant();
        t.Status = Status;
        t.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
    }
}
