using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;

namespace CrmSystem.Models.ViewModels;

/// <summary>Add / edit company (popup form).</summary>
public class ClientFormViewModel
{
    public int? ClientId { get; set; }

    [StringLength(20, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.CompanyCode")]
    public string? CompanyCode { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(200, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.CompanyName")]
    public string CompanyName { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.CompanyNameAr")]
    public string? CompanyNameAr { get; set; }

    [StringLength(100, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Industry")]
    public string? Industry { get; set; }

    [StringLength(30, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Phone")]
    public string? Phone { get; set; }

    [StringLength(256, ErrorMessage = "Validation.MaxLength")]
    [EmailAddress(ErrorMessage = "Validation.Email")]
    [Display(Name = "Field.Email")]
    public string? Email { get; set; }

    [StringLength(256, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Website")]
    public string? Website { get; set; }

    [StringLength(500, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Address")]
    public string? Address { get; set; }

    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(100, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.Country")]
    public string Country { get; set; } = "Oman";

    [StringLength(100, ErrorMessage = "Validation.MaxLength")]
    [Display(Name = "Field.City")]
    public string? City { get; set; }

    [Display(Name = "Field.Status")]
    public ClientStatus Status { get; set; } = ClientStatus.Active;

    [Display(Name = "Field.Description")]
    public string? Description { get; set; }

    [Display(Name = "Field.AccountManager")]
    public string? AccountManagerId { get; set; }

    [Display(Name = "Field.AssignedUsers")]
    public List<string> AssignedUserIds { get; set; } = new();

    public string? RowVersion { get; set; }

    // ----- display only -----
    public string? AccountManagerName { get; set; }
    public bool CanManageOwnership { get; set; }
    public List<(string Id, string Name)> UserOptions { get; set; } = new();
    public List<string> Industries { get; set; } = new();

    public static ClientFormViewModel FromEntity(Client c) => new()
    {
        ClientId = c.ClientId,
        CompanyCode = c.CompanyCode,
        CompanyName = c.CompanyName,
        CompanyNameAr = c.CompanyNameAr,
        Industry = c.Industry,
        Phone = c.Phone,
        Email = c.Email,
        Website = c.Website,
        Address = c.Address,
        Country = c.Country,
        City = c.City,
        Status = c.Status,
        Description = c.Description,
        AccountManagerId = c.AccountManagerId,
        AssignedUserIds = c.Assignments.Select(a => a.UserId).ToList(),
        RowVersion = Convert.ToBase64String(c.RowVersion)
    };

    public void ApplyTo(Client c, bool canManageOwnership)
    {
        if (!string.IsNullOrWhiteSpace(CompanyCode)) c.CompanyCode = CompanyCode.Trim().ToUpperInvariant();
        c.CompanyName = Client.CleanName(CompanyName);
        c.CompanyNameAr = string.IsNullOrWhiteSpace(CompanyNameAr) ? null : Client.CleanName(CompanyNameAr);
        c.Industry = Trim(Industry);
        c.Phone = Trim(Phone);
        c.Email = Trim(Email)?.ToLowerInvariant();
        c.Website = Trim(Website);
        c.Address = Trim(Address);
        c.Country = Trim(Country) ?? "Oman";
        c.City = Trim(City);
        c.Status = Status;
        c.Description = Trim(Description);
        if (canManageOwnership) c.AccountManagerId = string.IsNullOrWhiteSpace(AccountManagerId) ? null : AccountManagerId;
    }

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

/// <summary>Client profile page.</summary>
public class ClientProfileViewModel
{
    public Client Client { get; set; } = null!;
    public ReadModels.ClientSummary Summary { get; set; } = null!;
    public List<Contact> Contacts { get; set; } = new();
    public string? AccountManagerName { get; set; }
    public List<string> AssignedNames { get; set; } = new();
    public int OpenProjects { get; set; }
    public int UpcomingFollowUps { get; set; }
    public int OverdueFollowUps { get; set; }
    public int CompletedFollowUps { get; set; }
    public int Quotations { get; set; }
    public int Tenders { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
}
