using CrmSystem.Data;
using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Entities;
using CrmSystem.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// People inside companies. Several contacts per company, at most one primary
/// (making a contact primary automatically un-marks the previous one).
/// </summary>
public class ContactsController : AppController
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GridRequest grid, int? clientId, CancellationToken ct)
    {
        var arabic = IsArabic;
        var visible = await VisibleClientsAsync();
        var editable = CurrentUser.CanSeeAllData;
        var uid = UserId;

        var query = Db.Contacts.AsNoTracking().Where(c => visible.Any(v => v.ClientId == c.ClientId));
        if (clientId.HasValue) query = query.Where(c => c.ClientId == clientId.Value);
        if (grid.SearchTerm is { } q)
            query = query.Where(c => c.FullName.Contains(q) || (c.JobTitle != null && c.JobTitle.Contains(q))
                                  || (c.Department != null && c.Department.Contains(q)) || (c.Email != null && c.Email.Contains(q))
                                  || (c.Phone != null && c.Phone.Contains(q)) || (c.Mobile != null && c.Mobile.Contains(q))
                                  || c.Client.CompanyName.Contains(q));

        var rows = query.Select(c => new ContactRow
        {
            ContactId = c.ContactId,
            ClientId = c.ClientId,
            CompanyName = arabic && c.Client.CompanyNameAr != null ? c.Client.CompanyNameAr : c.Client.CompanyName,
            FullName = c.FullName,
            JobTitle = c.JobTitle,
            Department = c.Department,
            Phone = c.Phone,
            Mobile = c.Mobile,
            Email = c.Email,
            IsPrimary = c.IsPrimary,
            CanEdit = editable || c.Client.AccountManagerId == uid || c.Client.Assignments.Any(a => a.UserId == uid && a.CanEdit)
        });
        return Json(await rows.ApplySort(grid, nameof(ContactRow.FullName), tieBreaker: nameof(ContactRow.ContactId)).ToGridResultAsync(grid, ct));
    }

    public sealed class ContactRow
    {
        public int ContactId { get; set; }
        public int ClientId { get; set; }
        public string CompanyName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? JobTitle { get; set; }
        public string? Department { get; set; }
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public bool IsPrimary { get; set; }
        public bool CanEdit { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? clientId)
    {
        var model = new ContactFormViewModel { ClientId = clientId };
        if (clientId.HasValue)
        {
            if (!await CanAccessClientAsync(clientId.Value)) return NotAllowed();
            model.ClientName = await ClientNameAsync(clientId.Value);
            model.IsPrimary = !await Db.Contacts.AnyAsync(c => c.ClientId == clientId.Value);
        }
        return PartialView("_Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ContactFormViewModel model, CancellationToken ct)
    {
        model.ContactId = null;
        if (model.ClientId.HasValue && !await CanEditClientAsync(model.ClientId.Value, ct)) return NotAllowed();
        if (!ModelState.IsValid)
        {
            if (model.ClientId.HasValue) model.ClientName = await ClientNameAsync(model.ClientId.Value);
            return FormView("_Form", model);
        }

        var contact = new Contact();
        model.ApplyTo(contact);
        await Db.InTransactionAsync(async () =>
        {
            if (contact.IsPrimary) await ClearPrimaryAsync(contact.ClientId, null, ct);
            Db.Contacts.Add(contact);
            await Db.SaveChangesAsync(ct);
        }, ct);
        return Saved(T["Contacts.Created", contact.FullName], new { id = contact.ContactId, name = contact.FullName });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var contact = await Db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.ContactId == id, ct);
        if (contact == null) return NotFoundJson();
        if (!await CanEditClientAsync(contact.ClientId, ct)) return NotAllowed();
        return PartialView("_Form", ContactFormViewModel.FromEntity(contact, await ClientNameAsync(contact.ClientId)));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ContactFormViewModel model, CancellationToken ct)
    {
        var contact = await Db.Contacts.FirstOrDefaultAsync(c => c.ContactId == id, ct);
        if (contact == null) return NotFoundJson();
        if (!await CanEditClientAsync(contact.ClientId, ct)) return NotAllowed();
        if (model.ClientId.HasValue && model.ClientId != contact.ClientId && !await CanEditClientAsync(model.ClientId.Value, ct)) return NotAllowed();
        model.ContactId = id;
        if (!ModelState.IsValid)
        {
            if (model.ClientId.HasValue) model.ClientName = await ClientNameAsync(model.ClientId.Value);
            return FormView("_Form", model);
        }

        SetOriginalRowVersion(contact, model.RowVersion);
        model.ApplyTo(contact);
        await Db.InTransactionAsync(async () =>
        {
            if (contact.IsPrimary) await ClearPrimaryAsync(contact.ClientId, contact.ContactId, ct);
            await Db.SaveChangesAsync(ct);
        }, ct);
        return Saved(T["Contacts.Updated", contact.FullName]);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var contact = await Db.Contacts.FirstOrDefaultAsync(c => c.ContactId == id, ct);
        if (contact == null) return NotFoundJson();
        if (!await CanEditClientAsync(contact.ClientId, ct)) return NotAllowed();
        contact.IsPrimary = false; // frees the "one primary" slot
        Db.Contacts.Remove(contact); // soft delete
        await Db.SaveChangesAsync(ct);
        return Saved(T["Contacts.Deleted"]);
    }

    // ---------------------------------------------------------------- helpers
    private async Task ClearPrimaryAsync(int clientId, int? exceptContactId, CancellationToken ct)
    {
        var others = await Db.Contacts.Where(c => c.ClientId == clientId && c.IsPrimary && c.ContactId != (exceptContactId ?? 0)).ToListAsync(ct);
        if (others.Count == 0) return;
        others.ForEach(c => c.IsPrimary = false);
        await Db.SaveChangesAsync(ct); // separate statement so the unique "one primary" index is never violated
    }

    private async Task<bool> CanEditClientAsync(int clientId, CancellationToken ct)
    {
        if (!await CanAccessClientAsync(clientId)) return false;
        if (CurrentUser.CanSeeAllData) return true;
        return await Db.Clients.AnyAsync(c => c.ClientId == clientId &&
            (c.AccountManagerId == UserId || c.Assignments.Any(a => a.UserId == UserId && a.CanEdit)), ct);
    }

    private async Task<string?> ClientNameAsync(int clientId)
    {
        var arabic = IsArabic;
        return await Db.Clients.Where(c => c.ClientId == clientId)
            .Select(c => arabic && c.CompanyNameAr != null ? c.CompanyNameAr : c.CompanyName).FirstOrDefaultAsync();
    }
}
