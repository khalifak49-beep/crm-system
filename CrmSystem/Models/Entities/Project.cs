using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Common;
using CrmSystem.Models.Enums;
using CrmSystem.Models.Identity;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Business opportunity / project. Table: dbo.Projects
/// (CreatedDate in the requirements = CreatedAt.)
/// </summary>
public class Project : IAuditable, ISoftDelete
{
    public int ProjectId { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [Required, StringLength(200)]
    public string ProjectName { get; set; } = string.Empty;

    public string? ProjectDescription { get; set; }

    [Range(0, 999999999999999.999)]
    public decimal? ProjectValue { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "OMR";

    public DateOnly? ExpectedStartDate { get; set; }
    public DateOnly? ExpectedEndDate { get; set; }

    public ProjectStatus ProjectStatus { get; set; } = ProjectStatus.New;

    /// <summary>Win probability 0..100 %.</summary>
    [Range(0, 100)]
    public byte Probability { get; set; } = 10;

    public string? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    public DateOnly? ClosedDate { get; set; }

    [StringLength(500)]
    public string? LostReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<ProjectNote> Notes { get; set; } = new List<ProjectNote>();
    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
    public ICollection<FollowUp> FollowUps { get; set; } = new List<FollowUp>();
    public ICollection<Quotation> Quotations { get; set; } = new List<Quotation>();
    public ICollection<Tender> Tenders { get; set; } = new List<Tender>();

    /// <summary>Open = not Won and not Lost.</summary>
    public bool IsOpen => ProjectStatus is not (ProjectStatus.Won or ProjectStatus.Lost);
}
