namespace CrmSystem.Models.Common;

/// <summary>
/// Entities with CreatedAt/CreatedBy/UpdatedAt/UpdatedBy columns.
/// ApplicationDbContext fills these automatically on SaveChanges
/// and writes a row to AuditLogs.
/// All times are UTC.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}
