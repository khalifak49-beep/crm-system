using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Common;

namespace CrmSystem.Models.Entities;

/// <summary>
/// Note written on a project. Table: dbo.ProjectNotes
/// Soft delete uses IsDeleted only (the table has no DeletedAt/DeletedBy columns;
/// those two interface properties are ignored in the EF mapping).
/// </summary>
public class ProjectNote : IAuditable, ISoftDelete
{
    public int ProjectNoteId { get; set; }

    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    [Required]
    public string NoteText { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }   // not mapped
    public string? DeletedBy { get; set; }     // not mapped
}
