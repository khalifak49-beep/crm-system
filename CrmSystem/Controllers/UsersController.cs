using CrmSystem.Infrastructure.Grid;
using CrmSystem.Models.Entities;
using CrmSystem.Models.Identity;
using CrmSystem.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrmSystem.Controllers;

/// <summary>
/// User management (Admin only): create, edit, role, manager, activate / deactivate,
/// reset password, unlock. Safety rules: you cannot deactivate or demote yourself,
/// and the last active Admin can never be removed.
/// </summary>
[Authorize(Policy = AppPolicies.AdminOnly)]
public class UsersController : AppController
{
    private readonly UserManager<ApplicationUser> _users;

    public UsersController(UserManager<ApplicationUser> users) => _users = users;

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] GridRequest grid, string? role, bool? active, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var query = Db.Users.AsNoTracking().AsQueryable();
        if (active.HasValue) query = query.Where(u => u.IsActive == active.Value);
        if (!string.IsNullOrEmpty(role))
        {
            var roleId = await Db.Roles.Where(r => r.Name == role).Select(r => r.Id).FirstOrDefaultAsync(ct);
            query = query.Where(u => Db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
        }
        if (grid.SearchTerm is { } q)
            query = query.Where(u => u.FullName.Contains(q) || (u.FullNameAr != null && u.FullNameAr.Contains(q))
                                  || (u.UserName != null && u.UserName.Contains(q)) || (u.Email != null && u.Email.Contains(q)));

        var rows = query.Select(u => new UserRow
        {
            Id = u.Id,
            FullName = u.FullName,
            FullNameAr = u.FullNameAr,
            UserName = u.UserName ?? "",
            Email = u.Email,
            JobTitle = u.JobTitle,
            Role = Db.UserRoles.Where(ur => ur.UserId == u.Id).Join(Db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).FirstOrDefault(),
            ManagerName = u.Manager != null ? u.Manager.FullName : null,
            IsActive = u.IsActive,
            IsLocked = u.LockoutEnd != null && u.LockoutEnd > now,
            LastLoginAtUtc = u.LastLoginAtUtc
        });
        return Json(await rows.ApplySort(grid, nameof(UserRow.FullName), tieBreaker: nameof(UserRow.Id)).ToGridResultAsync(grid, ct));
    }

    public sealed class UserRow
    {
        public string Id { get; set; } = "";
        public string FullName { get; set; } = "";
        public string? FullNameAr { get; set; }
        public string UserName { get; set; } = "";
        public string? Email { get; set; }
        public string? JobTitle { get; set; }
        public string? Role { get; set; }
        public string? ManagerName { get; set; }
        public bool IsActive { get; set; }
        public bool IsLocked { get; set; }
        public DateTime? LastLoginAtUtc { get; set; }
    }

    // ---------------------------------------------------------------- create
    [HttpGet]
    public IActionResult Create() => PartialView("_Form", new UserFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(UserFormViewModel model, CancellationToken ct)
    {
        model.Id = null;
        if (string.IsNullOrEmpty(model.Password)) ModelState.AddModelError(nameof(model.Password), T["Validation.Required", T["Field.Password"]]);
        if (!AppRoles.All.Contains(model.Role)) ModelState.AddModelError(nameof(model.Role), T["Validation.InvalidShort"]);
        if (!ModelState.IsValid) return await FormAsync(model);

        var user = new ApplicationUser { CreatedBy = UserId, EmailConfirmed = true, LockoutEnabled = true };
        Apply(model, user);
        var result = await _users.CreateAsync(user, model.Password!);
        if (!result.Succeeded) { AddErrors(result); return await FormAsync(model); }

        await _users.AddToRoleAsync(user, model.Role);
        Db.AddAudit(AuditActions.Create, nameof(ApplicationUser), user.Id, new { user.UserName, user.Email, user.FullName, model.Role, user.IsActive });
        await Db.SaveChangesAsync(ct);
        return Saved(T["Users.Created", user.FullName]);
    }

    // ---------------------------------------------------------------- edit
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFoundJson();
        var roles = await _users.GetRolesAsync(user);
        var model = new UserFormViewModel
        {
            Id = user.Id,
            UserName = user.UserName ?? "",
            Email = user.Email ?? "",
            FullName = user.FullName,
            FullNameAr = user.FullNameAr,
            JobTitle = user.JobTitle,
            PhoneNumber = user.PhoneNumber,
            Role = roles.FirstOrDefault() ?? AppRoles.SalesEmployee,
            ManagerId = user.ManagerId,
            PreferredLanguage = user.PreferredLanguage,
            IsActive = user.IsActive,
            ReceiveEmailNotifications = user.ReceiveEmailNotifications,
            ReceiveBrowserNotifications = user.ReceiveBrowserNotifications
        };
        return await FormAsync(model, keepStatus: true);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(string id, UserFormViewModel model, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFoundJson();
        model.Id = id;
        var isSelf = id == UserId;
        var currentRoles = await _users.GetRolesAsync(user);
        var wasAdmin = currentRoles.Contains(AppRoles.Admin);

        if (!AppRoles.All.Contains(model.Role)) ModelState.AddModelError(nameof(model.Role), T["Validation.InvalidShort"]);
        if (isSelf && (!model.IsActive || model.Role != AppRoles.Admin)) ModelState.AddModelError(string.Empty, T["Users.CannotChangeSelf"]);
        if (wasAdmin && (model.Role != AppRoles.Admin || !model.IsActive) && await ActiveAdminCountAsync(ct) <= 1)
            ModelState.AddModelError(string.Empty, T["Users.LastAdmin"]);
        if (model.ManagerId == id) model.ManagerId = null;
        if (!ModelState.IsValid) return await FormAsync(model);

        var before = new { user.UserName, user.Email, user.FullName, user.IsActive, Role = currentRoles.FirstOrDefault() };
        var deactivated = user.IsActive && !model.IsActive;
        Apply(model, user);
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = UserId;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded) { AddErrors(result); return await FormAsync(model); }

        if (!currentRoles.Contains(model.Role))
        {
            await _users.RemoveFromRolesAsync(user, currentRoles);
            await _users.AddToRoleAsync(user, model.Role);
            Db.AddAudit(AuditActions.RoleChanged, nameof(ApplicationUser), user.Id, new { model.Role }, new { Role = currentRoles.FirstOrDefault() });
        }
        // Force re-login so role / deactivation take effect immediately
        if (deactivated || !currentRoles.Contains(model.Role)) await _users.UpdateSecurityStampAsync(user);

        Db.AddAudit(AuditActions.Update, nameof(ApplicationUser), user.Id, new { user.UserName, user.Email, user.FullName, user.IsActive, model.Role }, before);
        await Db.SaveChangesAsync(ct);
        return Saved(T["Users.Updated", user.FullName]);
    }

    // ---------------------------------------------------------------- password / lock / active
    [HttpGet]
    public async Task<IActionResult> ResetPassword(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFoundJson();
        return PartialView("_ResetPassword", new ResetPasswordViewModel { Id = id, FullName = user.FullName });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(string id, ResetPasswordViewModel model, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFoundJson();
        model.Id = id;
        model.FullName = user.FullName;
        if (!ModelState.IsValid) return FormView("_ResetPassword", model);

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, model.NewPassword);
        if (!result.Succeeded) { AddErrors(result, nameof(model.NewPassword)); return FormView("_ResetPassword", model); }
        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);
        Db.AddAudit(AuditActions.PasswordChanged, nameof(ApplicationUser), user.Id, new { ResetBy = User.Identity?.Name });
        await Db.SaveChangesAsync(ct);
        return Saved(T["Users.PasswordReset", user.FullName]);
    }

    [HttpPost]
    public async Task<IActionResult> ToggleActive(string id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFoundJson();
        if (id == UserId) return Fail(T["Users.CannotChangeSelf"]);
        if (user.IsActive && await _users.IsInRoleAsync(user, AppRoles.Admin) && await ActiveAdminCountAsync(ct) <= 1)
            return Fail(T["Users.LastAdmin"]);

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdatedBy = UserId;
        await _users.UpdateAsync(user);
        await _users.UpdateSecurityStampAsync(user); // sign the user out everywhere
        Db.AddAudit(AuditActions.Update, nameof(ApplicationUser), user.Id, new { user.IsActive });
        await Db.SaveChangesAsync(ct);
        return Saved(user.IsActive ? T["Users.Activated", user.FullName] : T["Users.Deactivated", user.FullName]);
    }

    [HttpPost]
    public async Task<IActionResult> Unlock(string id, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFoundJson();
        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);
        Db.AddAudit(AuditActions.Update, nameof(ApplicationUser), user.Id, new { Unlocked = true });
        await Db.SaveChangesAsync(ct);
        return Saved(T["Users.Unlocked", user.FullName]);
    }

    // ---------------------------------------------------------------- helpers
    private Task<int> ActiveAdminCountAsync(CancellationToken ct) =>
        Db.Users.CountAsync(u => u.IsActive && Db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == AppRoles.AdminRoleId), ct);

    private static void Apply(UserFormViewModel m, ApplicationUser u)
    {
        u.UserName = m.UserName.Trim();
        u.Email = m.Email.Trim().ToLowerInvariant();
        u.FullName = m.FullName.Trim();
        u.FullNameAr = string.IsNullOrWhiteSpace(m.FullNameAr) ? null : m.FullNameAr.Trim();
        u.JobTitle = string.IsNullOrWhiteSpace(m.JobTitle) ? null : m.JobTitle.Trim();
        u.PhoneNumber = string.IsNullOrWhiteSpace(m.PhoneNumber) ? null : m.PhoneNumber.Trim();
        u.ManagerId = string.IsNullOrEmpty(m.ManagerId) ? null : m.ManagerId;
        u.PreferredLanguage = m.PreferredLanguage == "ar" ? "ar" : "en";
        u.IsActive = m.IsActive;
        u.ReceiveEmailNotifications = m.ReceiveEmailNotifications;
        u.ReceiveBrowserNotifications = m.ReceiveBrowserNotifications;
    }

    private void AddErrors(IdentityResult result, string? key = null)
    {
        foreach (var e in result.Errors)
        {
            var translated = T["Identity." + e.Code];
            ModelState.AddModelError(key ?? (e.Code.Contains("Password") ? nameof(UserFormViewModel.Password) : e.Code.Contains("Email") ? nameof(UserFormViewModel.Email) : e.Code.Contains("UserName") ? nameof(UserFormViewModel.UserName) : string.Empty),
                translated.ResourceNotFound ? e.Description : translated.Value);
        }
    }

    private async Task<IActionResult> FormAsync(UserFormViewModel model, bool keepStatus = false)
    {
        model.IsSelf = model.Id == UserId;
        if (!string.IsNullOrEmpty(model.ManagerId))
            model.ManagerName = await Db.Users.Where(u => u.Id == model.ManagerId).Select(u => u.FullName).FirstOrDefaultAsync();
        return keepStatus ? PartialView("_Form", model) : FormView("_Form", model);
    }
}
