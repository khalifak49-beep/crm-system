using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Common;
using CrmSystem.Models.Enums;
using CrmSystem.Models.Identity;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Price quotation sent to a client. Table: dbo.Quotations
/// QuotationNumber is unique across the system.
/// </summary>
public class Quotation : IAuditable, ISoftDelete
{
    public int QuotationId { get; set; }

    [Required, StringLength(50)]
    public string QuotationNumber { get; set; } = string.Empty;

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }

    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? TenderId { get; set; }
    public Tender? Tender { get; set; }

    [StringLength(250)]
    public string? Title { get; set; }

    /// <summary>Date sent.</summary>
    public DateOnly? SubmissionDate { get; set; }

    [Range(0, 999999999999999.999)]
    public decimal? QuotationValue { get; set; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "OMR";

    /// <summary>Expiry date.</summary>
    public DateOnly? ValidUntil { get; set; }

    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;

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
}
