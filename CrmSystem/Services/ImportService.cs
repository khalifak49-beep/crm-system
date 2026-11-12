using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using CrmSystem.Data;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Services;

/// <summary>
/// Imports the legacy Excel sheet:
///     Date | Client | Cold Calls | Emails | Visit | Facility Tour | Note
/// Rules:
///   - every marked column (1, x, yes, ✓ ...) becomes ONE separate activity of that type
///   - a row with only a note becomes one "Other" activity so no note is ever lost
///   - notes are copied exactly as written; the original date is kept
///   - unknown companies are created (Source = ExcelImport)
///   - duplicates (same company + date + type + note, in the file or already in the database) are reported
///   - the import runs inside ONE database transaction (all or nothing)
/// Flow: PreviewAsync (stores the file + ImportBatch "Previewed") -> ConfirmAsync.
/// </summary>
public sealed class ImportService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ImportService> _logger;

    public ImportService(ApplicationDbContext db, IWebHostEnvironment env, ILogger<ImportService> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    // ------------------------------------------------------------------ models
    public sealed class ImportActivity
    {
        public int TypeId { get; set; }
        public bool Duplicate { get; set; }
    }

    public sealed class ImportRow
    {
        public int RowNumber { get; set; }
        public DateOnly? Date { get; set; }
        public string? RawDate { get; set; }
        public string? ClientName { get; set; }
        public string? ContactName { get; set; }
        public string? Note { get; set; }
        public bool NewClient { get; set; }
        public List<ImportActivity> Activities { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public bool IsValid => Errors.Count == 0;
        public bool IsDuplicate => IsValid && Activities.Count > 0 && Activities.All(a => a.Duplicate);
    }

    public sealed class ImportPreview
    {
        public int BatchId { get; set; }
        public string FileName { get; set; } = "";
        public DateTime? AlreadyImportedOn { get; set; }
        public List<string> HeaderErrors { get; set; } = new();
        public Dictionary<string, string> DetectedColumns { get; set; } = new();
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int ErrorRows { get; set; }
        public int DuplicateRows { get; set; }
        public int ActivitiesToCreate { get; set; }
        public int DuplicateActivities { get; set; }
        public List<string> NewClients { get; set; } = new();
        public int NewContacts { get; set; }
        public List<ImportRow> Rows { get; set; } = new();
    }

    public sealed class ImportResult
    {
        public int BatchId { get; set; }
        public int ClientsCreated { get; set; }
        public int ContactsCreated { get; set; }
        public int ActivitiesCreated { get; set; }
        public int SkippedRows { get; set; }
        public int DuplicatesSkipped { get; set; }
        public int ErrorRows { get; set; }
    }

    private string Folder
    {
        get
        {
            var folder = Path.Combine(_env.ContentRootPath, "App_Data", "imports");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    private string StoredPath(int batchId) => Path.Combine(Folder, $"batch-{batchId}.xlsx");

    // ------------------------------------------------------------------ preview
    public async Task<ImportPreview> PreviewAsync(Stream upload, string fileName, string userId, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await upload.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        await CleanupStalePreviewsAsync(ct);
        var preview = await AnalyzeAsync(bytes, ct);
        preview.FileName = Path.GetFileName(fileName);
        preview.AlreadyImportedOn = await _db.ImportBatches.AsNoTracking()
            .Where(b => b.FileHash == hash && b.Status == ImportBatchStatus.Completed)
            .OrderByDescending(b => b.CompletedAtUtc).Select(b => b.CompletedAtUtc).FirstOrDefaultAsync(ct);

        if (preview.HeaderErrors.Count > 0) return preview;

        var batch = new ImportBatch
        {
            FileName = preview.FileName.Length > 260 ? preview.FileName[..260] : preview.FileName,
            FileHash = hash,
            FileSizeBytes = bytes.LongLength,
            Status = ImportBatchStatus.Previewed,
            TotalRows = preview.TotalRows,
            ErrorRows = preview.ErrorRows,
            DuplicateRows = preview.DuplicateRows,
            ImportedBy = userId,
            StartedAtUtc = DateTime.UtcNow
        };
        _db.ImportBatches.Add(batch);
        await _db.SaveChangesAsync(ct);
        await File.WriteAllBytesAsync(StoredPath(batch.ImportBatchId), bytes, ct);
        preview.BatchId = batch.ImportBatchId;
        return preview;
    }

    /// <summary>Previews that were never confirmed are removed after 24 hours (row + stored file).</summary>
    private async Task CleanupStalePreviewsAsync(CancellationToken ct)
    {
        var limit = DateTime.UtcNow.AddHours(-24);
        var stale = await _db.ImportBatches.Where(b => b.Status == ImportBatchStatus.Previewed && b.StartedAtUtc < limit).ToListAsync(ct);
        if (stale.Count == 0) return;
        foreach (var b in stale)
        {
            var path = StoredPath(b.ImportBatchId);
            if (File.Exists(path)) File.Delete(path);
        }
        _db.ImportBatches.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ confirm
    public async Task<ImportResult> ConfirmAsync(int batchId, string performerId, bool skipDuplicates, string userId, string? userName, CancellationToken ct)
    {
        var batch = await _db.ImportBatches.FirstOrDefaultAsync(b => b.ImportBatchId == batchId, ct)
                    ?? throw new InvalidOperationException("Import batch not found.");
        if (batch.Status != ImportBatchStatus.Previewed) throw new InvalidOperationException("This import was already processed.");
        var path = StoredPath(batchId);
        if (!File.Exists(path)) throw new InvalidOperationException("The uploaded file is no longer available. Upload it again.");

        var preview = await AnalyzeAsync(await File.ReadAllBytesAsync(path, ct), ct);
        if (preview.HeaderErrors.Count > 0) throw new InvalidOperationException(string.Join(" ", preview.HeaderErrors));

        var result = new ImportResult { BatchId = batchId, ErrorRows = preview.ErrorRows };
        _db.AuditEnabled = false; // one summary audit row instead of thousands

        try
        {
            await _db.InTransactionAsync(async () =>
            {
                var clients = await _db.Clients.Select(c => new { c.ClientId, c.NormalizedName }).ToListAsync(ct);
                var byName = clients.GroupBy(c => c.NormalizedName).ToDictionary(g => g.Key, g => g.First().ClientId);
                var newClients = new Dictionary<string, Client>();
                var knownContacts = (await _db.Contacts.Select(c => new { c.ContactId, c.ClientId, c.FullName }).ToListAsync(ct))
                    .GroupBy(c => $"{c.ClientId}|{c.FullName.Trim().ToUpperInvariant()}").ToDictionary(g => g.Key, g => g.First().ContactId);
                var clientsWithPrimary = (await _db.Contacts.Where(c => c.IsPrimary).Select(c => c.ClientId).ToListAsync(ct)).ToHashSet();
                var newContacts = new Dictionary<string, Contact>();

                foreach (var row in preview.Rows.Where(r => r.IsValid))
                {
                    var activities = row.Activities.Where(a => !(skipDuplicates && a.Duplicate)).ToList();
                    result.DuplicatesSkipped += row.Activities.Count - activities.Count;
                    if (activities.Count == 0) { result.SkippedRows++; continue; }

                    var key = Client.Normalize(row.ClientName);
                    int? clientId = byName.TryGetValue(key, out var existingId) ? existingId : null;
                    Client? newClient = null;
                    if (clientId == null && !newClients.TryGetValue(key, out newClient))
                    {
                        newClient = new Client
                        {
                            CompanyName = Client.CleanName(row.ClientName),
                            CompanyCode = null!,               // generated by SQL Server
                            Source = ClientSource.ExcelImport,
                            ImportBatchId = batchId,
                            AccountManagerId = performerId,
                            Status = ClientStatus.Active
                        };
                        newClients[key] = newClient;
                        _db.Clients.Add(newClient);
                    }

                    // contact person (optional column): reuse by name inside the company, otherwise create
                    int? contactId = null;
                    Contact? newContact = null;
                    if (row.ContactName != null)
                    {
                        var contactKey = clientId.HasValue ? $"{clientId}|{row.ContactName.ToUpperInvariant()}" : $"new:{key}|{row.ContactName.ToUpperInvariant()}";
                        if (knownContacts.TryGetValue(contactKey, out var cid)) contactId = cid;
                        else if (!newContacts.TryGetValue(contactKey, out newContact))
                        {
                            var hasPrimary = clientId.HasValue ? clientsWithPrimary.Contains(clientId.Value) : newContacts.Keys.Any(k => k.StartsWith($"new:{key}|"));
                            newContact = new Contact { FullName = row.ContactName, IsActive = true, IsPrimary = !hasPrimary };
                            if (clientId.HasValue)
                            {
                                newContact.ClientId = clientId.Value;
                                clientsWithPrimary.Add(clientId.Value);
                            }
                            else newContact.Client = newClient!;
                            newContacts[contactKey] = newContact;
                            _db.Contacts.Add(newContact);
                        }
                    }

                    foreach (var a in activities)
                    {
                        var activity = new Activity
                        {
                            ContactId = contactId,
                            ActivityTypeId = a.TypeId,
                            ActivityDate = row.Date!.Value,
                            Notes = row.Note,                  // exactly as in Excel
                            PerformedById = performerId,
                            ImportBatchId = batchId,
                            SourceRowNumber = row.RowNumber,
                            CreatedBy = userId
                        };
                        if (clientId.HasValue) activity.ClientId = clientId.Value;
                        else activity.Client = newClient!;
                        if (newContact != null) activity.Contact = newContact;
                        _db.Activities.Add(activity);
                        result.ActivitiesCreated++;
                    }
                }

                result.ClientsCreated = newClients.Count;
                result.ContactsCreated = newContacts.Count;
                await _db.SaveChangesAsync(ct);

                batch.Status = ImportBatchStatus.Completed;
                batch.DefaultPerformerId = performerId;
                batch.ImportedRows = preview.Rows.Count(r => r.IsValid) - result.SkippedRows;
                batch.SkippedRows = result.SkippedRows + preview.ErrorRows;
                batch.DuplicateRows = preview.DuplicateRows;
                batch.ErrorRows = preview.ErrorRows;
                batch.ClientsCreated = result.ClientsCreated;
                batch.ActivitiesCreated = result.ActivitiesCreated;
                batch.CompletedAtUtc = DateTime.UtcNow;
                batch.ErrorsJson = preview.ErrorRows == 0 ? null : JsonSerializer.Serialize(
                    preview.Rows.Where(r => !r.IsValid).Take(500).Select(r => new { row = r.RowNumber, errors = r.Errors }));

                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    UserName = userName,
                    Action = AuditActions.Import,
                    EntityName = nameof(ImportBatch),
                    EntityId = batchId.ToString(),
                    NewValues = JsonSerializer.Serialize(new { batch.FileName, result.ClientsCreated, result.ContactsCreated, result.ActivitiesCreated, result.SkippedRows, result.DuplicatesSkipped, result.ErrorRows }),
                    CreatedAtUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(ct);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel import {BatchId} failed - nothing was saved.", batchId);
            _db.ChangeTracker.Clear();
            var failed = await _db.ImportBatches.FirstAsync(b => b.ImportBatchId == batchId, ct);
            failed.Status = ImportBatchStatus.Failed;
            failed.CompletedAtUtc = DateTime.UtcNow;
            failed.ErrorsJson = JsonSerializer.Serialize(new[] { new { row = 0, errors = new[] { ex.GetBaseException().Message } } });
            await _db.SaveChangesAsync(ct);
            throw;
        }

        return result;
    }

    // ------------------------------------------------------------------ parsing
    private static readonly Dictionary<string, string[]> HeaderAliases = new()
    {
        ["date"] = new[] { "date", "activitydate", "التاريخ", "تاريخ" },
        ["client"] = new[] { "client", "company", "customer", "clientname", "companyname", "الشركة", "العميل", "اسمالشركة" },
        ["calls"] = new[] { "coldcalls", "coldcall", "calls", "call", "phonecall", "phonecalls", "مكالمات", "مكالمة", "اتصال" },
        ["emails"] = new[] { "emails", "email", "mail", "e-mail", "e-mails", "بريد", "ايميل", "إيميل", "البريد" },
        ["visit"] = new[] { "visit", "visits", "زيارة", "زيارات" },
        ["tour"] = new[] { "facilitytour", "facilitytours", "tour", "tours", "sitetour", "جولة", "جولات" },
        ["note"] = new[] { "note", "notes", "comment", "comments", "remarks", "ملاحظات", "ملاحظة" },
        ["contact"] = new[] { "contact", "contactperson", "contactname", "person", "personname", "الشخص", "جهةالاتصال", "الشخصالمسؤول", "اسمالشخص", "المسؤول" }
    };

    private static string NormalizeHeader(string? s) =>
        new string((s ?? "").Trim().ToLowerInvariant().Where(ch => !char.IsWhiteSpace(ch) && ch != '_' && ch != '-' && ch != '.').ToArray());

    private static readonly HashSet<string> MarkWords = new(StringComparer.OrdinalIgnoreCase)
        { "x", "yes", "y", "true", "✓", "✔", "√", "نعم", "done", "ok" };

    private static bool IsMarked(IXLCell cell)
    {
        if (cell.IsEmpty()) return false;
        if (cell.DataType == XLDataType.Number) return cell.GetDouble() > 0;
        if (cell.DataType == XLDataType.Boolean) return cell.GetBoolean();
        var text = cell.GetString().Trim();
        if (text.Length == 0) return false;
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) return n > 0;
        return MarkWords.Contains(text);
    }

    private static readonly string[] DateFormats =
        { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yy", "d/M/yy", "d MMM yyyy", "dd MMM yyyy", "d-MMM-yyyy", "dd-MMM-yy" };

    private static DateOnly? ParseDate(IXLCell cell, out string raw)
    {
        raw = cell.GetFormattedString();
        if (cell.IsEmpty()) return null;
        try
        {
            if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());
            if (cell.DataType == XLDataType.Number)
            {
                var serial = cell.GetDouble();
                if (serial > 20000 && serial < 80000) return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            }
        }
        catch (Exception) { /* fall through to text parsing */ }
        var text = cell.GetString().Trim();
        if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d))
            return DateOnly.FromDateTime(d);
        return null;
    }

    /// <summary>Parses the workbook and checks rows / duplicates (no database writes).</summary>
    private async Task<ImportPreview> AnalyzeAsync(byte[] bytes, CancellationToken ct)
    {
        var preview = new ImportPreview();
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(new MemoryStream(bytes));
        }
        catch (Exception)
        {
            preview.HeaderErrors.Add("Import.Error.NotExcel");
            return preview;
        }

        using (workbook)
        {
            var ws = workbook.Worksheets.FirstOrDefault();
            if (ws == null || ws.LastRowUsed() == null)
            {
                preview.HeaderErrors.Add("Import.Error.Empty");
                return preview;
            }

            // Find the header row (first 10 rows) and map columns
            int headerRow = 0;
            var map = new Dictionary<string, int>();
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (var r = 1; r <= Math.Min(10, ws.LastRowUsed()!.RowNumber()) && headerRow == 0; r++)
            {
                var found = new Dictionary<string, int>();
                for (var c = 1; c <= lastCol; c++)
                {
                    var header = NormalizeHeader(ws.Cell(r, c).GetString());
                    if (header.Length == 0) continue;
                    foreach (var (key, aliases) in HeaderAliases)
                        if (!found.ContainsKey(key) && aliases.Contains(header)) found[key] = c;
                }
                if (found.ContainsKey("date") && found.ContainsKey("client")) { headerRow = r; map = found; }
            }

            if (headerRow == 0)
            {
                preview.HeaderErrors.Add("Import.Error.Headers");
                return preview;
            }
            if (!map.Keys.Any(k => k is "calls" or "emails" or "visit" or "tour" or "note"))
            {
                preview.HeaderErrors.Add("Import.Error.NoActivityColumns");
                return preview;
            }
            foreach (var (key, col) in map) preview.DetectedColumns[key] = ws.Cell(headerRow, col).GetString().Trim();

            var typeColumns = new List<(int Column, int TypeId)>();
            if (map.TryGetValue("calls", out var cc)) typeColumns.Add((cc, ActivityTypeIds.ColdCall));
            if (map.TryGetValue("emails", out var ec)) typeColumns.Add((ec, ActivityTypeIds.Email));
            if (map.TryGetValue("visit", out var vc)) typeColumns.Add((vc, ActivityTypeIds.Visit));
            if (map.TryGetValue("tour", out var tc)) typeColumns.Add((tc, ActivityTypeIds.FacilityTour));

            var lastRow = ws.LastRowUsed()!.RowNumber();
            for (var r = headerRow + 1; r <= lastRow; r++)
            {
                var rowCells = ws.Row(r);
                if (rowCells.IsEmpty()) continue;

                var row = new ImportRow { RowNumber = r };
                row.Date = ParseDate(ws.Cell(r, map["date"]), out var raw);
                row.RawDate = raw;
                row.ClientName = Client.CleanName(ws.Cell(r, map["client"]).GetString());
                if (map.TryGetValue("contact", out var pc))
                {
                    var person = ws.Cell(r, pc).GetString().Trim();
                    row.ContactName = person.Length == 0 ? null : (person.Length > 150 ? person[..150] : person);
                }
                if (map.TryGetValue("note", out var nc))
                {
                    var note = ws.Cell(r, nc).GetString();
                    row.Note = string.IsNullOrWhiteSpace(note) ? null : note; // kept exactly
                }
                foreach (var (column, typeId) in typeColumns)
                    if (IsMarked(ws.Cell(r, column))) row.Activities.Add(new ImportActivity { TypeId = typeId });

                if (row.Activities.Count == 0 && row.Note == null && string.IsNullOrEmpty(row.ClientName) && string.IsNullOrWhiteSpace(raw))
                    continue; // fully empty row

                if (!row.Date.HasValue) row.Errors.Add(string.IsNullOrWhiteSpace(raw) ? "Import.Error.MissingDate" : "Import.Error.InvalidDate");
                if (string.IsNullOrEmpty(row.ClientName)) row.Errors.Add("Import.Error.MissingClient");
                else if (row.ClientName.Length > 200) row.Errors.Add("Import.Error.ClientTooLong");
                if (row.Activities.Count == 0)
                {
                    if (row.Note != null) row.Activities.Add(new ImportActivity { TypeId = ActivityTypeIds.Other }); // keep the note
                    else row.Errors.Add("Import.Error.NoActivity");
                }
                preview.Rows.Add(row);
            }
        }

        // ---- existing companies & duplicates
        var names = preview.Rows.Where(r => r.IsValid).Select(r => Client.Normalize(r.ClientName)).Distinct().ToList();
        var existing = await _db.Clients.AsNoTracking().Where(c => names.Contains(c.NormalizedName))
            .Select(c => new { c.ClientId, c.NormalizedName }).ToListAsync(ct);
        var existingByName = existing.GroupBy(c => c.NormalizedName).ToDictionary(g => g.Key, g => g.First().ClientId);

        var dbKeys = new HashSet<string>();
        var validRows = preview.Rows.Where(r => r.IsValid).ToList();
        if (existing.Count > 0 && validRows.Count > 0)
        {
            var ids = existing.Select(c => c.ClientId).ToList();
            var min = validRows.Min(r => r.Date!.Value);
            var max = validRows.Max(r => r.Date!.Value);
            var dbActivities = await _db.Activities.AsNoTracking()
                .Where(a => ids.Contains(a.ClientId) && a.ActivityDate >= min && a.ActivityDate <= max)
                .Select(a => new { a.ClientId, a.ActivityDate, a.ActivityTypeId, a.Notes }).ToListAsync(ct);
            foreach (var a in dbActivities) dbKeys.Add($"{a.ClientId}|{a.ActivityDate:yyyyMMdd}|{a.ActivityTypeId}|{a.Notes}");
        }

        var fileKeys = new HashSet<string>();
        foreach (var row in validRows)
        {
            var name = Client.Normalize(row.ClientName);
            var hasClient = existingByName.TryGetValue(name, out var clientId);
            row.NewClient = !hasClient;
            foreach (var a in row.Activities)
            {
                var fileKey = $"{name}|{row.Date:yyyyMMdd}|{a.TypeId}|{row.Note}";
                var inFile = !fileKeys.Add(fileKey);
                var inDb = hasClient && dbKeys.Contains($"{clientId}|{row.Date:yyyyMMdd}|{a.TypeId}|{row.Note}");
                a.Duplicate = inFile || inDb;
            }
        }

        preview.TotalRows = preview.Rows.Count;
        preview.ErrorRows = preview.Rows.Count(r => !r.IsValid);
        preview.ValidRows = preview.TotalRows - preview.ErrorRows;
        preview.DuplicateRows = preview.Rows.Count(r => r.IsDuplicate);
        preview.ActivitiesToCreate = validRows.Sum(r => r.Activities.Count(a => !a.Duplicate));
        preview.DuplicateActivities = validRows.Sum(r => r.Activities.Count(a => a.Duplicate));
        var existingIds = existing.Select(e => e.ClientId).ToList();
        var existingContacts = existingIds.Count == 0 ? new HashSet<string>()
            : (await _db.Contacts.AsNoTracking().Where(c => existingIds.Contains(c.ClientId))
                .Select(c => new { c.ClientId, c.FullName }).ToListAsync(ct))
              .Select(c => $"{c.ClientId}|{c.FullName.Trim().ToUpperInvariant()}").ToHashSet();
        preview.NewContacts = validRows.Where(r => r.ContactName != null)
            .Select(r => existingByName.TryGetValue(Client.Normalize(r.ClientName), out var id)
                ? $"{id}|{r.ContactName!.ToUpperInvariant()}"
                : $"new:{Client.Normalize(r.ClientName)}|{r.ContactName!.ToUpperInvariant()}")
            .Distinct().Count(k => !existingContacts.Contains(k));
        preview.NewClients = validRows.Where(r => r.NewClient).Select(r => r.ClientName!)
            .GroupBy(Client.Normalize).Select(g => g.First()).OrderBy(n => n).ToList();
        return preview;
    }

    /// <summary>Empty template with the expected headers (for users starting fresh).</summary>
    public static byte[] Template()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Activities");
        var headers = new[] { "Date", "Client", "Contact", "Cold Calls", "Emails", "Visit", "Facility Tour", "Note" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#EAF2FE"));
        }
        ws.Column(1).Style.DateFormat.Format = "dd/mm/yyyy";
        ws.Columns(1, 7).Width = 16;
        ws.Column(2).Width = 30;
        ws.Column(3).Width = 24;
        ws.Column(8).Width = 70;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
