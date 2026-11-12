using System.ComponentModel.DataAnnotations;
using CrmSystem.Models.Enums;
using CrmSystem.Models.Identity;

namespace CrmSystem.Models.Entities;

/// <summary>
/// One Excel import operation. Table: dbo.ImportBatches
/// </summary>
public class ImportBatch
{
    public int ImportBatchId { get; set; }

    [Required, StringLength(260)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>SHA-256 (64 hex chars) - detects importing the same file twice.</summary>
    [Required, StringLength(64, MinimumLength = 64)]
    public string FileHash { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Previewed;

    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int SkippedRows { get; set; }
    public int DuplicateRows { get; set; }
    public int ErrorRows { get; set; }
    public int ClientsCreated { get; set; }
    public int ActivitiesCreated { get; set; }

    /// <summary>Employee credited with the imported activities.</summary>
    public string? DefaultPerformerId { get; set; }
    public ApplicationUser? DefaultPerformer { get; set; }

    /// <summary>JSON array of row errors.</summary>
    public string? ErrorsJson { get; set; }

    [Required]
    public string ImportedBy { get; set; } = string.Empty;
    public ApplicationUser ImportedByUser { get; set; } = null!;

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public ICollection<Client> Clients { get; set; } = new List<Client>();
    public ICollection<Activity> Activities { get; set; } = new List<Activity>();
}
