using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Common;
using CrmSystem.Models.Identity;

namespace CrmSystem.Models.Entities;

/// <summary>
/// One communication with a client (call, email, visit...). Table: dbo.Activities
/// Every communication is a separate row - never merged.
/// ActivityDate/ActivityTime are LOCAL (Asia/Muscat) as entered by the user.
/// </summary>
public class Activity : IAuditable, ISoftDelete
{
    public int ActivityId { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public int ActivityTypeId { get; set; }
    public ActivityType ActivityType { get; set; } = null!;

    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    public int? TenderId { get; set; }
    public Tender? Tender { get; set; }

    public DateOnly ActivityDate { get; set; }
    public TimeOnly? ActivityTime { get; set; }

    [StringLength(250)]
    public string? Subject { get; set; }

    /// <summary>Full notes (NVARCHAR(MAX)) - never truncated.</summary>
    public string? Notes { get; set; }

    [StringLength(500)]
    public string? Outcome { get; set; }

    /// <summary>Employee credited with this activity (performance reports).</summary>
    [Required]
    public string PerformedById { get; set; } = string.Empty;
    public ApplicationUser PerformedBy { get; set; } = null!;

    public int? ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }

    /// <summary>Excel row number when imported.</summary>
    public int? SourceRowNumber { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>Follow-ups scheduled from this activity.</summary>
    public ICollection<FollowUp> CreatedFollowUps { get; set; } = new List<FollowUp>();
}
