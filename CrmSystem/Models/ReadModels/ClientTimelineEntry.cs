namespace CrmSystem.Models.ReadModels;

/// <summary>
/// Read-only row of view dbo.vw_ClientTimeline.
/// EventKind: Activity, FollowUp, Project, Quotation, Tender.
/// EventAtLocal is Asia/Muscat local time.
/// Order with: OrderByDescending(e => e.EventAtLocal).ThenByDescending(e => e.SortKey)
/// </summary>
public class ClientTimelineEntry
{
    public int ClientId { get; set; }
    public DateTime EventAtLocal { get; set; }
    public string EventKind { get; set; } = string.Empty;
    public string EventCode { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    public string TitleAr { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? Outcome { get; set; }
    public string IconName { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public int ReferenceId { get; set; }
    public string? UserId { get; set; }
    public long SortKey { get; set; }
}
