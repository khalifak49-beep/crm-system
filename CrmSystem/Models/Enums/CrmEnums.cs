namespace CrmSystem.Models.Enums;

/*
 * All enums are stored as TINYINT in SQL Server.
 * The numeric values MUST match the CHECK constraints in Database/03_CoreTables.sql.
 * Display text (English / Arabic) comes from resource files in Phase 3.
 */

public enum ClientStatus : byte
{
    Active = 1,
    Inactive = 2,
    Prospect = 3,
    Archived = 4
}

public enum ClientSource : byte
{
    Manual = 1,
    ExcelImport = 2
}

public enum ProjectStatus : byte
{
    New = 1,
    Discussion = 2,
    QuotationSent = 3,
    Tender = 4,
    Negotiation = 5,
    Won = 6,
    Lost = 7,
    OnHold = 8
}

public enum FollowUpStatus : byte
{
    Pending = 1,
    Completed = 2,
    Cancelled = 3,
    Overdue = 4
}

public enum FollowUpPriority : byte
{
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4
}

public enum ReminderStatus : byte
{
    Scheduled = 1,
    Sent = 2,
    /// <summary>Follow-up was created less than the reminder period before its due time; reminder sent immediately.</summary>
    SentLate = 3,
    /// <summary>Follow-up completed/cancelled before the reminder time.</summary>
    Skipped = 4,
    Failed = 5
}

public enum QuotationStatus : byte
{
    Draft = 1,
    Sent = 2,
    UnderReview = 3,
    Accepted = 4,
    Rejected = 5,
    Expired = 6,
    Cancelled = 7
}

public enum TenderStatus : byte
{
    Open = 1,
    Preparing = 2,
    Submitted = 3,
    UnderEvaluation = 4,
    Awarded = 5,
    Lost = 6,
    Cancelled = 7,
    Expired = 8
}

public enum NotificationType : byte
{
    FollowUpReminder = 1,
    FollowUpLateReminder = 2,
    FollowUpAssigned = 3,
    FollowUpOverdue = 4,
    QuotationExpiry = 5,
    TenderClosing = 6,
    ImportCompleted = 7,
    System = 8
}

public enum NotificationChannel : byte
{
    InApp = 1,
    Email = 2,
    Browser = 3
}

public enum NotificationLogStatus : byte
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    Skipped = 4
}

public enum ImportBatchStatus : byte
{
    Previewed = 1,
    Completed = 2,
    Failed = 3,
    RolledBack = 4
}

/// <summary>
/// Fixed Ids of the system activity types seeded by Database/05_SeedData.sql.
/// Admins may add more types; these nine always exist.
/// </summary>
public static class ActivityTypeIds
{
    public const int ColdCall = 1;
    public const int Email = 2;
    public const int Visit = 3;
    public const int FacilityTour = 4;
    public const int Meeting = 5;
    public const int Quotation = 6;
    public const int ProjectDiscussion = 7;
    public const int TenderFollowUp = 8;
    public const int Other = 9;
}
