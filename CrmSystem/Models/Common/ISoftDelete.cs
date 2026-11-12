namespace CrmSystem.Models.Common;

/// <summary>
/// Entities that are never physically deleted.
/// Calling db.Remove(entity) is converted by ApplicationDbContext into
/// IsDeleted = 1 + DeletedAt + DeletedBy, and a global query filter
/// hides deleted rows from every query (use IgnoreQueryFilters() to see them).
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
