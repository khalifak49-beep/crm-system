namespace CrmSystem.Models.ViewModels;

/// <summary>Initial page data for Dashboard/Index (statistics are loaded by AJAX).</summary>
public class DashboardPageViewModel
{
    public bool CanSeeAllData { get; set; }
    public string TodayLocal { get; set; } = string.Empty;   // yyyy-MM-dd in Asia/Muscat
    public string? CurrentUserId { get; set; }
}

public class DashboardStats
{
    public int TotalClients { get; set; }
    public int ActiveClients { get; set; }
    public int Calls { get; set; }
    public int Emails { get; set; }
    public int Visits { get; set; }
    public int FacilityTours { get; set; }
    public int Meetings { get; set; }
    public int UpcomingFollowUps { get; set; }
    public int OverdueFollowUps { get; set; }
    public int TodayFollowUps { get; set; }
    public int PotentialProjects { get; set; }
    public int WonProjects { get; set; }
    public int LostProjects { get; set; }
}

public record ChartPoint(string Label, int Value, string? Id = null);
