using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Common;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Bilingual lookup of activity types. Table: dbo.ActivityTypes
/// Ids 1..9 are system types (see ActivityTypeIds).
/// </summary>
public class ActivityType : IAuditable
{
    public int ActivityTypeId { get; set; }

    /// <summary>COLD_CALL, EMAIL, VISIT ...</summary>
    [Required, StringLength(30)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string NameEn { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string NameAr { get; set; } = string.Empty;

    /// <summary>Framework7 icon name (iOS style), e.g. "phone", "envelope".</summary>
    [Required, StringLength(50)]
    public string IconName { get; set; } = "circle";

    /// <summary>Apple system color, e.g. #34C759.</summary>
    [Required, StringLength(7, MinimumLength = 7)]
    public string ColorHex { get; set; } = "#8E8E93";

    /// <summary>Column header in the legacy Excel file (Cold Calls, Emails, Visit, Facility Tour).</summary>
    [StringLength(50)]
    public string? ExcelColumnName { get; set; }

    public int SortOrder { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public string Name(bool arabic) => arabic ? NameAr : NameEn;
}
