using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Common;
using CrmSystem.Models.Enums;
using CrmSystem.Models.Identity;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Tender issued by a client. Table: dbo.Tenders
/// TenderNumber is unique per client (each client issues its own numbers).
/// ClosingDate is LOCAL Asia/Muscat time.
/// </summary>
public class Tender : IAuditable, ISoftDelete
{
    public int TenderId { get; set; }

    [Required, StringLength(50)]
    public string TenderNumber { get; set; } = string.Empty;

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    [Required, StringLength(250)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateOnly? IssueDate { get; set; }

    /// <summary>Date we submitted.</summary>
    public DateOnly? SubmissionDate { get; set; }

    /// <summary>Deadline / expiry (local time).</summary>
    public DateTime? ClosingDate { get; set; }

    [Range(0, 999999999999999.999)]
    public decimal? TenderValue { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "OMR";

    public TenderStatus Status { get; set; } = TenderStatus.Open;

    public string? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<Quotation> Quotations { get; set; } = new List<Quotation>();
}
