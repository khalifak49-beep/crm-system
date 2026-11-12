using CrmSystem.Models.Enums;

namespace CrmSystem.Models.ReadModels;

/// <summary>
/// Read-only row of view dbo.vw_ClientSummary (Clients grid).
/// </summary>
public class ClientSummary
{
    public int ClientId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyNameAr { get; set; }
    public string? Industry { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string Country { get; set; } = string.Empty;
    public string? City { get; set; }
    public ClientStatus Status { get; set; }
    public string? AccountManagerId { get; set; }
    public string? AccountManagerName { get; set; }

    public int? PrimaryContactId { get; set; }
    public string? PrimaryContactName { get; set; }
    public string? PrimaryContactJobTitle { get; set; }
    public string? PrimaryContactPhone { get; set; }
    public string? PrimaryContactEmail { get; set; }

    public DateOnly? LastActivityDate { get; set; }
    public int? LastActivityTypeId { get; set; }
    public long TotalActivities { get; set; }

    public int? NextFollowUpId { get; set; }
    public DateOnly? NextFollowUpDate { get; set; }
    public TimeOnly? NextFollowUpTime { get; set; }
    public DateTime? NextFollowUpAtUtc { get; set; }

    public long OverdueFollowUps { get; set; }
    public long OpenProjects { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
