using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Entities;

namespace CrmSystem.Models.ViewModels;

public class ContactFormViewModel
{
    public int? ContactId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [Display(Name = "Field.Company")]
    public int? ClientId { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(150, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.FullName")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.JobTitle")]
    public string? JobTitle { get; set; }

    [StringLength(100, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Department")]
    public string? Department { get; set; }

    [StringLength(30, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Phone")]
    public string? Phone { get; set; }

    [StringLength(30, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Mobile")]
    public string? Mobile { get; set; }

    [StringLength(256, ErrorMessage = "Validation.MaxLength")]
    [EmailAddress(ErrorMessage = "Validation.Email")]
    [Display(Name = "Field.Email")]
    public string? Email { get; set; }

    [Display(Name = "Field.Notes")]
    public string? Notes { get; set; }

    [Display(Name = "Contacts.IsPrimary")]
    public bool IsPrimary { get; set; }

    [Display(Name = "Field.Active")]
    public bool IsActive { get; set; } = true;

    public string? RowVersion { get; set; }

    // display
    public string? ClientName { get; set; }

    public static ContactFormViewModel FromEntity(Contact c, string? clientName) => new()
    {
        ContactId = c.ContactId,
        ClientId = c.ClientId,
        FullName = c.FullName,
        JobTitle = c.JobTitle,
        Department = c.Department,
        Phone = c.Phone,
        Mobile = c.Mobile,
        Email = c.Email,
        Notes = c.Notes,
        IsPrimary = c.IsPrimary,
        IsActive = c.IsActive,
        RowVersion = Convert.ToBase64String(c.RowVersion),
        ClientName = clientName
    };

    public void ApplyTo(Contact c)
    {
        c.ClientId = ClientId!.Value;
        c.FullName = FullName.Trim();
        c.JobTitle = Clean(JobTitle);
        c.Department = Clean(Department);
        c.Phone = Clean(Phone);
        c.Mobile = Clean(Mobile);
        c.Email = Clean(Email)?.ToLowerInvariant();
        c.Notes = Clean(Notes);
        c.IsPrimary = IsPrimary;
        c.IsActive = IsActive;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
