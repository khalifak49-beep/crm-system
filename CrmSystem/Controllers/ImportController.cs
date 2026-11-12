using CrmSystem.Infrastructure;
using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Identity;
using CrmSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>Import of the legacy Excel file (Admin / Sales Manager).</summary>
[Authorize(Policy = AppPolicies.ManagerOrAdmin)]
public class ImportController : AppController
{
    private readonly ImportService _import;
    private static readonly string[] AllowedExtensions = { ".xlsx", ".xlsm" };
    private const long MaxBytes = 10 * 1024 * 1024;

    public ImportController(ImportService import) => _import = import;

    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>POST (multipart) file -> preview JSON (nothing imported yet).</summary>
    [HttpPost]
    [RequestSizeLimit(MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Preview(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return Fail(T["Import.Error.NoFile"]);
        if (file.Length > MaxBytes) return Fail(T["Import.Error.TooLarge"]);
        if (!AllowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant())) return Fail(T["Import.Error.NotExcel"]);

        await using var stream = file.OpenReadStream();
        var preview = await _import.PreviewAsync(stream, file.FileName, UserId, ct);
        if (preview.HeaderErrors.Count > 0) return Fail(string.Join(" ", preview.HeaderErrors.Select(e => T[e].Value)));

        var typeNames = await Db.ActivityTypes.AsNoTracking().ToDictionaryAsync(t => t.ActivityTypeId, t => IsArabic ? t.NameAr : t.NameEn, ct);
        return Json(new
        {
            ok = true,
            preview.BatchId,
            preview.FileName,
            alreadyImportedOn = preview.AlreadyImportedOn,
            preview.DetectedColumns,
            preview.TotalRows,
            preview.ValidRows,
            preview.ErrorRows,
            preview.DuplicateRows,
            preview.ActivitiesToCreate,
            preview.DuplicateActivities,
            newClients = preview.NewClients,
            preview.NewContacts,
            rows = preview.Rows.Take(1000).Select(r => new
            {
                r.RowNumber,
                date = r.Date,
                r.RawDate,
                r.ClientName,
                r.ContactName,
                r.NewClient,
                r.Note,
                activities = r.Activities.Select(a => new { type = typeNames.GetValueOrDefault(a.TypeId, "?"), a.TypeId, a.Duplicate }),
                errors = r.Errors.Select(e => T[e].Value),
                r.IsValid,
                r.IsDuplicate
            })
        });
    }

    public sealed class ConfirmRequest
    {
        public int BatchId { get; set; }
        public string? PerformerId { get; set; }
        public bool SkipDuplicates { get; set; } = true;
    }

    [HttpPost]
    public async Task<IActionResult> Confirm([FromBody] ConfirmRequest request, CancellationToken ct)
    {
        var performer = string.IsNullOrEmpty(request.PerformerId) ? UserId : request.PerformerId;
        if (!await Db.Users.AnyAsync(u => u.Id == performer && u.IsActive, ct)) return Fail(T["Import.Error.Performer"]);
        try
        {
            var result = await _import.ConfirmAsync(request.BatchId, performer, request.SkipDuplicates, UserId, User.Identity?.Name, ct);
            return Saved(T["Import.Done", result.ActivitiesCreated, result.ClientsCreated], result);
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> History([FromQuery] GridRequest grid, CancellationToken ct)
    {
        var rows = Db.ImportBatches.AsNoTracking().Select(b => new BatchRow
        {
            ImportBatchId = b.ImportBatchId,
            FileName = b.FileName,
            Status = (int)b.Status,
            TotalRows = b.TotalRows,
            ClientsCreated = b.ClientsCreated,
            ActivitiesCreated = b.ActivitiesCreated,
            ErrorRows = b.ErrorRows,
            DuplicateRows = b.DuplicateRows,
            ImportedBy = b.ImportedByUser.FullName,
            StartedAtUtc = b.StartedAtUtc
        });
        return Json(await rows.ApplySort(grid, nameof(BatchRow.ImportBatchId), defaultDescending: true).ToGridResultAsync(grid, ct));
    }

    public sealed class BatchRow
    {
        public int ImportBatchId { get; set; }
        public string FileName { get; set; } = "";
        public int Status { get; set; }
        public int TotalRows { get; set; }
        public int ClientsCreated { get; set; }
        public int ActivitiesCreated { get; set; }
        public int ErrorRows { get; set; }
        public int DuplicateRows { get; set; }
        public string ImportedBy { get; set; } = "";
        public DateTime StartedAtUtc { get; set; }
    }

    [HttpGet]
    public IActionResult Template() =>
        File(ImportService.Template(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "crm-import-template.xlsx");
}
