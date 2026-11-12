using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Common;
using CrmSystem.Models.Enums;
using CrmSystem.Models.Identity;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Company / client. Table: dbo.Clients
/// Duplicate policy: CompanyCode and NormalizedName are unique among non-deleted rows.
/// </summary>
public class Client : IAuditable, ISoftDelete
{
    public int ClientId { get; set; }

    /// <summary>
    /// Leave null on insert to let SQL Server generate CL-00001, CL-00002...
    /// </summary>
    [StringLength(20)]
    public string CompanyCode { get; set; } = null!;

    [Required, StringLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [StringLength(200)]
    public string? CompanyNameAr { get; set; }

    /// <summary>Computed by SQL Server: UPPER(TRIM(CompanyName)). Read-only.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    [StringLength(100)]
    public string? Industry { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(256), EmailAddress]
    public string? Email { get; set; }

    [StringLength(256)]
    public string? Website { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [Required, StringLength(100)]
    public string Country { get; set; } = "Oman";

    [StringLength(100)]
    public string? City { get; set; }

    public ClientStatus Status { get; set; } = ClientStatus.Active;

    public string? Description { get; set; }

    public string? AccountManagerId { get; set; }
    public ApplicationUser? AccountManager { get; set; }

    public ClientSource Source { get; set; } = ClientSource.Manual;

    public int? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    // Audit
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // Navigation
    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
    public ICollection<FollowUp> FollowUps { get; set; } = new List<FollowUp>();
    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<Quotation> Quotations { get; set; } = new List<Quotation>();
    public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
    public ICollection<ClientAssignment> Assignments { get; set; } = new List<ClientAssignment>();

    /// <summary>
    /// Same normalization the database uses, plus collapsing double spaces.
    /// Use before saving and when searching for duplicates (e.g. Excel import).
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var collapsed = System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s+", " ");
        return collapsed.ToUpperInvariant();
    }

    /// <summary>Clean the display name: trim + collapse multiple spaces.</summary>
    public static string CleanName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s+", " ");
}
