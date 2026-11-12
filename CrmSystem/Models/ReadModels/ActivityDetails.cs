namespace CrmSystem.Models.ReadModels;

/// <summary>
/// Read-only row of view dbo.vw_ActivityDetails (Activities grid + reports).
/// </summary>
public class ActivityDetails
{
    public int ActivityId { get; set; }
    public int ClientId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyNameAr { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public int? ContactId { get; set; }
    public string? ContactName { get; set; }

    public int ActivityTypeId { get; set; }
    public string ActivityTypeCode { get; set; } = string.Empty;
    public string ActivityTypeNameEn { get; set; } = string.Empty;
    public string ActivityTypeNameAr { get; set; } = string.Empty;
    public string ActivityTypeIcon { get; set; } = string.Empty;
    public string ActivityTypeColor { get; set; } = string.Empty;

    public DateOnly ActivityDate { get; set; }
    public TimeOnly? ActivityTime { get; set; }
    public int ActivityYear { get; set; }
    public int ActivityMonth { get; set; }

    public string? Subject { get; set; }
    public string? Notes { get; set; }
    public string? Outcome { get; set; }

    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public int? QuotationId { get; set; }
    public int? TenderId { get; set; }

    public string PerformedById { get; set; } = string.Empty;
    public string PerformedByName { get; set; } = string.Empty;

    public int? ImportBatchId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
}
